// Phase 5.2 — on-GPU frame-statistics reduction.
//
// A single thread group of 256 threads strides over every pixel, accumulating the
// per-pixel Welford variance, sample count, clamp flag, history weight, rejection
// flag and hit mask into group-shared partials, then a log-step reduction collapses
// them to one thread which writes the finished frame aggregates (averages,
// percentages, observed max sample count) into an 8-float readback buffer.
//
// This keeps per-pixel data on the GPU (pitfall P6) — only ~32 bytes leave per frame.
// A line-for-line port of Phase5Reference.Reduce, whose formulas mirror the reduction
// at the tail of JobSystem.TaaResolver.ResolveDisplayBufferWithTaa. Requires SM 6.0+.

#define GROUP 256u

// The inputs are bound as UAVs (read-only here) so they can stay in the
// UnorderedAccess state the trace/resolve leave them in — no state transitions,
// only a UAV barrier for ordering.
RWStructuredBuffer<uint>  SampleCount   : register(u0); // effective sample count
RWStructuredBuffer<float> LumaM2        : register(u1); // Welford M2 of luma
RWStructuredBuffer<uint>  LastHit       : register(u2); // 1 = pixel hit
RWStructuredBuffer<uint>  ClampHitFrame : register(u3); // 1 = clamped this frame
RWStructuredBuffer<float> HistoryWeight : register(u4); // per-pixel history blend weight
RWStructuredBuffer<uint>  Rejected      : register(u5); // 1 = history rejected

// 8 floats: [0] avg variance, [1] avg spp, [2] clamped %, [3] rejected %,
//           [4] avg history weight, [5] hit %, [6] max spp, [7] pad.
RWStructuredBuffer<float> Stats : register(u6);

cbuffer ReduceConstants : register(b0)
{
    uint PixelCount; uint _rpad0; uint _rpad1; uint _rpad2;
};

groupshared float gVar[GROUP];
groupshared float gSpp[GROUP];
groupshared float gHist[GROUP];
groupshared uint  gRej[GROUP];
groupshared uint  gClamp[GROUP];
groupshared uint  gHit[GROUP];
groupshared uint  gMax[GROUP];

[numthreads(GROUP, 1, 1)]
void CSMain(uint3 gtid : SV_GroupThreadID)
{
    uint tid = gtid.x;

    float vSum = 0.0, sppSum = 0.0, histSum = 0.0;
    uint rej = 0u, clamped = 0u, hits = 0u, maxSpp = 0u;

    for (uint i = tid; i < PixelCount; i += GROUP)
    {
        uint c = SampleCount[i];
        float variance = c > 1u ? LumaM2[i] / float(c - 1u) : 0.0;
        vSum += variance;
        sppSum += float(c);
        maxSpp = max(maxSpp, c);
        histSum += HistoryWeight[i];
        if (Rejected[i] != 0u) rej++;
        if (ClampHitFrame[i] != 0u) clamped++;
        if (LastHit[i] != 0u) hits++;
    }

    gVar[tid] = vSum; gSpp[tid] = sppSum; gHist[tid] = histSum;
    gRej[tid] = rej; gClamp[tid] = clamped; gHit[tid] = hits; gMax[tid] = maxSpp;
    GroupMemoryBarrierWithGroupSync();

    for (uint s = GROUP / 2u; s > 0u; s >>= 1)
    {
        if (tid < s)
        {
            gVar[tid] += gVar[tid + s];
            gSpp[tid] += gSpp[tid + s];
            gHist[tid] += gHist[tid + s];
            gRej[tid] += gRej[tid + s];
            gClamp[tid] += gClamp[tid + s];
            gHit[tid] += gHit[tid + s];
            gMax[tid] = max(gMax[tid], gMax[tid + s]);
        }
        GroupMemoryBarrierWithGroupSync();
    }

    if (tid == 0u)
    {
        float pixels = max(float(PixelCount), 1.0);
        Stats[0] = gVar[0] / pixels;
        Stats[1] = gSpp[0] / pixels;
        Stats[2] = float(gClamp[0]) / pixels * 100.0;
        Stats[3] = float(gRej[0]) / pixels * 100.0;
        Stats[4] = gHist[0] / pixels;
        Stats[5] = float(gHit[0]) / pixels * 100.0;
        Stats[6] = float(gMax[0]);
        Stats[7] = 0.0;
    }
}
