// Phase 5 — resolve pass of the temporal pipeline, with volumetric compositing
// (as Phase 4) and a debug-view switch.
//
// The surface temporal math + fog composite are identical to ResolvePhase4.hlsl
// (a line-for-line port of Phase3Reference.cs). Phase 5 additionally:
//   * tracks the per-pixel history blend weight and rejection flag the temporal
//     path already decides (mirroring Phase3Reference.ResolvePixel's out params),
//   * reads the extra per-pixel buffers the debug views need (effective sample
//     count, the direct/indirect accumulation splits, the albedo AOV packed into
//     NormalIn.w), and
//   * ends with a DebugMode switch (a port of Phase5Reference.Colorize) that writes
//     the selected visualization instead of Beauty.
//
// DebugMode 0 (Beauty) reproduces Phase 4 exactly. Requires SM 6.0+.

#define MIN_VALID_WEIGHT    0.25

// ── Bindings ──────────────────────────────────────────────────────────

RWStructuredBuffer<float4> Accum          : register(u0); // xyz accumulated mean (read)
RWStructuredBuffer<uint>   SampleCountBuf : register(u1); // effective sample count (read)
RWStructuredBuffer<float>  LumaM2         : register(u2); // Welford M2 of luma (read)
RWStructuredBuffer<uint>   LastHit        : register(u4); // 1 = pixel hit (read)
RWStructuredBuffer<float4> DirectAccum    : register(u5); // xyz direct mean (read)
RWStructuredBuffer<float4> IndirectAccum  : register(u6); // xyz indirect mean (read)
RWStructuredBuffer<float4> HitPointIn     : register(u7); // xyz world hit point (read)
RWStructuredBuffer<float4> NormalIn       : register(u8); // xyz oriented normal, w albedo (read)
RWStructuredBuffer<float4> FogIn          : register(u9); // xyz inscatter*correction, w transmittance (read)

RWStructuredBuffer<float4> HistoryXyzIn   : register(u10); // previous resolved (read)
RWStructuredBuffer<float4> HistoryHitIn   : register(u11); // previous hit point (read)
RWStructuredBuffer<uint>   HistoryValidIn : register(u12); // previous hit mask (read)

RWStructuredBuffer<float4> HistoryXyzOut  : register(u13); // next resolved (write)
RWStructuredBuffer<float4> HistoryHitOut  : register(u14); // next hit point (write)
RWStructuredBuffer<uint>   HistoryValidOut: register(u15); // next hit mask (write)

RWStructuredBuffer<float>  ClampAmountIn    : register(u16); // cumulative clamp amount (read)
RWStructuredBuffer<float>  HistoryWeightOut : register(u17); // per-pixel history weight (write, for the reduction)
RWStructuredBuffer<uint>   RejectedOut      : register(u18); // per-pixel rejection flag (write, for the reduction)

RWTexture2D<float4>        Output         : register(u3); // resolved sRGB image (write)
StructuredBuffer<uint>     MazeGrid       : register(t0); // overhead-map wall bitmap (plan §7)
StructuredBuffer<float4>   DecalPixels    : register(t1); // decal atlas — rat sprite (plan §8)

#define DECAL_SIZE 128u  // must match PropTextures.Size

cbuffer ResolveConstants : register(b0)
{
    float3 PrevCamPos;   float _rpad0;
    float4 PrevCamRot;   // previous-frame camera quaternion (x, y, z, w)
    float  TanHalfFov;   float AspectTanHalfFov; float TemporalBlendAlpha; float ReprojThreshold;
    uint   Width;        uint  Height; uint UseTaa; uint IsMoving;
    uint   FilterRadius; uint  DebugMode; float VarianceNorm; uint ShowMap; // §7 overhead map
    float3 CurrCamPos;   float SampleCountNorm;
    uint   MazeGw;       uint  MazeGh; uint PlayerGx; uint PlayerGy;         // §7 minimap dims + player cell
    float4 CurrCamRot;                                                       // §8 rat projection
    float3 RatPos;       float RatSize;                                      // §8 rat billboard
    uint   ShowRat;      uint  RatLayer; float FadeLevel; float ClassicDepthCue; // §8 rat, §9.4 fade, §1.4 depth cue
};

// ── Spatial (box) filter — mirrors JobSystem.ResolveFilteredXYZ ─────────
// Motion-gated box blur of the accumulation buffer. Phase 5.3 dropped the
// edge-aware bilateral variant that Phase3Reference/ResolvePhase3-4 carry: the GPU
// port never enabled it (it was a never-wired denoise knob), so only the box path
// — the one the demo and self-tests actually exercise — is kept here.

float3 FilteredXYZ(int x, int y, int radius)
{
    if (radius <= 0)
        return Accum[y * int(Width) + x].xyz;

    int yMin = max(y - radius, 0);
    int yMax = min(y + radius, int(Height) - 1);
    int xMin = max(x - radius, 0);
    int xMax = min(x + radius, int(Width) - 1);

    float3 sum = float3(0.0, 0.0, 0.0);
    int count = 0;
    for (int ny = yMin; ny <= yMax; ny++)
    {
        int rowOff = ny * int(Width);
        for (int nx = xMin; nx <= xMax; nx++)
        {
            sum += Accum[rowOff + nx].xyz;
            count++;
        }
    }
    return sum / float(count);
}

void NeighborhoodMinMax(int x, int y, int radius, out float3 nMin, out float3 nMax)
{
    nMin = float3(1e30, 1e30, 1e30);
    nMax = float3(-1e30, -1e30, -1e30);
    int yMin = max(y - 1, 0);
    int yMax = min(y + 1, int(Height) - 1);
    int xMin = max(x - 1, 0);
    int xMax = min(x + 1, int(Width) - 1);
    for (int ny = yMin; ny <= yMax; ny++)
    {
        for (int nx = xMin; nx <= xMax; nx++)
        {
            float3 v = FilteredXYZ(nx, ny, radius);
            nMin = min(nMin, v);
            nMax = max(nMax, v);
        }
    }
}

// ── Reprojection — mirrors TaaResolver.TryProjectToPrevPixel ───────────

float3 RotateByQuaternion(float4 q, float3 v)
{
    float x2 = q.x + q.x;
    float y2 = q.y + q.y;
    float z2 = q.z + q.z;
    float wx2 = q.w * x2;
    float wy2 = q.w * y2;
    float wz2 = q.w * z2;
    float xx2 = q.x * x2;
    float xy2 = q.x * y2;
    float xz2 = q.x * z2;
    float yy2 = q.y * y2;
    float yz2 = q.y * z2;
    float zz2 = q.z * z2;

    return float3(
        v.x * (1.0 - yy2 - zz2) + v.y * (xy2 - wz2) + v.z * (xz2 + wy2),
        v.x * (xy2 + wz2) + v.y * (1.0 - xx2 - zz2) + v.z * (yz2 - wx2),
        v.x * (xz2 - wy2) + v.y * (yz2 + wx2) + v.z * (1.0 - xx2 - yy2));
}

bool TryProjectToPrevPixel(float3 worldPoint, out float px, out float py)
{
    float4 invPrevRot = float4(-PrevCamRot.xyz, PrevCamRot.w);
    float3 local = RotateByQuaternion(invPrevRot, worldPoint - PrevCamPos);
    if (local.z <= 1e-4)
    {
        px = 0.0;
        py = 0.0;
        return false;
    }

    float ndcX = local.x / (local.z * AspectTanHalfFov);
    float ndcY = local.y / (local.z * TanHalfFov);

    px = ((ndcX + 1.0) * 0.5) * float(Width) - 0.5;
    py = ((1.0 - ndcY) * 0.5) * float(Height) - 0.5;
    return px >= 0.0 && px < float(Width) && py >= 0.0 && py < float(Height);
}

// ── sRGB resolve (same matrix + transfer as Phase 1/2) ─────────────────

float LinearToSRGB(float c)
{
    if (c <= 0.0031308)
        return 12.92 * c;
    return 1.055 * pow(c, 1.0 / 2.4) - 0.055;
}

float3 ResolveToSRGB(float3 xyz)
{
    float lr = xyz.x * 3.2406 + xyz.y * (-1.5372) + xyz.z * (-0.4986);
    float lg = xyz.x * (-0.9689) + xyz.y * 1.8758 + xyz.z * 0.0415;
    float lb = xyz.x * 0.0557 + xyz.y * (-0.2040) + xyz.z * 1.0570;
    return float3(LinearToSRGB(lr), LinearToSRGB(lg), LinearToSRGB(lb));
}

// ── Debug palettes (line-for-line port of Phase5Reference) ─────────────

float3 MultiStop(float t, float3 c0, float3 c1, float3 c2, float3 c3, float3 c4)
{
    if (t <= 0.25) return lerp(c0, c1, t / 0.25);
    if (t <= 0.5)  return lerp(c1, c2, (t - 0.25) / 0.25);
    if (t <= 0.75) return lerp(c2, c3, (t - 0.5) / 0.25);
    return lerp(c3, c4, (t - 0.75) / 0.25);
}

float3 PaletteSampleCount(uint sampleCount, float maxSampleCount)
{
    float t = maxSampleCount > 0.0 ? clamp(float(sampleCount) / maxSampleCount, 0.0, 1.0) : 0.0;
    return MultiStop(t,
        float3(0.20, 0.00, 0.35),
        float3(0.00, 0.70, 1.00),
        float3(0.10, 0.90, 0.20),
        float3(1.00, 0.95, 0.20),
        float3(1.00, 1.00, 1.00));
}

float3 PaletteHistoryWeight(float weight)
{
    float t = clamp(weight, 0.0, 1.0);
    return MultiStop(t,
        float3(0.10, 0.10, 0.20),
        float3(0.20, 0.80, 1.00),
        float3(1.00, 0.95, 0.20),
        float3(1.00, 0.50, 0.10),
        float3(1.00, 0.10, 0.10));
}

float3 PaletteDepth(float depth)
{
    float t = 1.0 - exp(-depth * 0.08);
    return MultiStop(t,
        float3(0.02, 0.02, 0.05),
        float3(0.08, 0.15, 0.45),
        float3(0.10, 0.60, 0.80),
        float3(0.90, 0.90, 0.45),
        float3(1.00, 1.00, 1.00));
}

float3 PaletteVariance(float variance, float maxVariance)
{
    float t = maxVariance > 0.0 ? clamp(variance / maxVariance, 0.0, 1.0) : 0.0;
    return MultiStop(t,
        float3(0.00, 0.00, 0.20),
        float3(0.00, 0.20, 0.90),
        float3(0.00, 0.80, 0.30),
        float3(1.00, 0.90, 0.10),
        float3(1.00, 0.20, 0.10));
}

float3 PaletteClamp(float amount)
{
    float t = 1.0 - exp(-amount * 0.5);
    return MultiStop(t,
        float3(0.00, 0.00, 0.00),
        float3(1.00, 0.50, 0.10),
        float3(1.00, 0.15, 0.10),
        float3(1.00, 0.80, 0.70),
        float3(1.00, 1.00, 1.00));
}

float3 PaletteAlbedo(float albedo)
{
    float v = clamp(albedo, 0.0, 1.0);
    return float3(v, v, v);
}

float3 PaletteNormal(float3 normal)
{
    if (all(normal == float3(0.0, 0.0, 0.0)))
        return float3(0.0, 0.0, 0.0);
    return normalize(normal) * 0.5 + float3(0.5, 0.5, 0.5);
}

// ── Kernel ────────────────────────────────────────────────────────────

// The animated rat (plan §8) used to be a screen-space billboard composited here. It is now a
// first-class path-traced object (IntersectRat in the primary trace of PathTracePhase6), so it
// fogs, occludes, and reflects like real geometry with no resolve-side compositing.

// Overhead-map overlay (plan §7): a top-right corner minimap of the maze wall bitmap with
// a player marker. Screen-space, so it costs nothing except the corner pixels; gated by
// ShowMap (off keeps the resolve output identical to Phase 5).
float3 ApplyMinimap(float3 col, uint2 pix)
{
    if (ShowMap == 0u)
        return col;

    uint mapPx = max(96u, Height / 4u);
    uint margin = 14u;
    uint x0 = Width - mapPx - margin;
    uint y0 = margin;
    if (pix.x < x0 || pix.x >= x0 + mapPx || pix.y < y0 || pix.y >= y0 + mapPx)
        return col;

    // Stationary, North-up map, but with the X axis flipped (East on the left). The flip is
    // needed because this engine's camera is left-handed — a plain top-down would be a mirror
    // image of the first-person view, making left turns read as right. With X flipped the
    // map's chirality matches the view, so a left turn traces as a left turn, at every heading.
    float mx = float(pix.x - x0) / float(mapPx);
    float my = float(pix.y - y0) / float(mapPx);
    uint gx = min((uint)((1.0 - mx) * float(MazeGw)), MazeGw - 1u); // flipped: East→left, West→right
    uint gy = min((uint)(my * float(MazeGh)), MazeGh - 1u);         // North→top

    float3 mapCol = (MazeGrid[gy * MazeGw + gx] != 0u)
        ? float3(0.13, 0.13, 0.16)   // wall
        : float3(0.85, 0.85, 0.90);  // corridor
    if (gx == PlayerGx && gy == PlayerGy)
        mapCol = float3(0.95, 0.18, 0.18); // player

    float edge = 2.0;
    if (float(pix.x) < float(x0) + edge || float(pix.x) > float(x0 + mapPx) - edge ||
        float(pix.y) < float(y0) + edge || float(pix.y) > float(y0 + mapPx) - edge)
        mapCol = float3(0.04, 0.04, 0.04); // frame

    return lerp(col, mapCol, 0.85);
}

[numthreads(8, 8, 1)]
void CSMain(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= Width || tid.y >= Height)
        return;

    int x = int(tid.x);
    int y = int(tid.y);
    uint ix = tid.y * Width + tid.x;

    int radius = (IsMoving != 0u) ? int(FilterRadius) : 0;

    float alpha = clamp(TemporalBlendAlpha, 0.01, 1.0);
    if (IsMoving != 0u)
        alpha = clamp(alpha * 10.0, 0.01, 1.0);

    float3 current = FilteredXYZ(x, y, radius);
    float3 resolved = current;

    bool currentHit = (LastHit[ix] != 0u);
    float3 currentHitPoint = HitPointIn[ix].xyz;

    // Temporal signals the debug views surface (mirrors Phase3Reference.ResolvePixel).
    float historyWeight = 0.0;
    bool rejected = false;

    float pxf, pyf;
    if ((UseTaa != 0u) && currentHit && TryProjectToPrevPixel(currentHitPoint, pxf, pyf))
    {
        int ix0 = int(floor(pxf));
        int iy0 = int(floor(pyf));
        float wx = pxf - ix0;
        float wy = pyf - iy0;

        int x0 = clamp(ix0, 0, int(Width) - 1);
        int y0 = clamp(iy0, 0, int(Height) - 1);
        int x1 = clamp(ix0 + 1, 0, int(Width) - 1);
        int y1 = clamp(iy0 + 1, 0, int(Height) - 1);

        uint i00 = uint(y0) * Width + uint(x0);
        uint i10 = uint(y0) * Width + uint(x1);
        uint i01 = uint(y1) * Width + uint(x0);
        uint i11 = uint(y1) * Width + uint(x1);

        float w00 = (1.0 - wx) * (1.0 - wy);
        float w10 = wx * (1.0 - wy);
        float w01 = (1.0 - wx) * wy;
        float w11 = wx * wy;

        float validWeight =
            (HistoryValidIn[i00] != 0u ? w00 : 0.0) + (HistoryValidIn[i10] != 0u ? w10 : 0.0) +
            (HistoryValidIn[i01] != 0u ? w01 : 0.0) + (HistoryValidIn[i11] != 0u ? w11 : 0.0);

        if (validWeight > MIN_VALID_WEIGHT)
        {
            float3 history = HistoryXyzIn[i00].xyz * w00 + HistoryXyzIn[i10].xyz * w10 +
                             HistoryXyzIn[i01].xyz * w01 + HistoryXyzIn[i11].xyz * w11;
            float3 historyHit = HistoryHitIn[i00].xyz * w00 + HistoryHitIn[i10].xyz * w10 +
                                HistoryHitIn[i01].xyz * w01 + HistoryHitIn[i11].xyz * w11;

            float3 dHit = historyHit - currentHitPoint;
            float reprojErr = dot(dHit, dHit);
            bool accept = reprojErr < ReprojThreshold;

            float3 histNormal = NormalIn[i00].xyz * w00 + NormalIn[i10].xyz * w10 +
                                NormalIn[i01].xyz * w01 + NormalIn[i11].xyz * w11;
            float3 currNormal = NormalIn[ix].xyz;
            if (accept && any(histNormal != 0.0) && any(currNormal != 0.0))
            {
                float ndot = clamp(dot(normalize(histNormal), normalize(currNormal)), -1.0, 1.0);
                if (ndot < 0.90)
                    accept = false;
            }

            if (accept)
            {
                float3 nMin, nMax;
                NeighborhoodMinMax(x, y, radius, nMin, nMax);
                history = clamp(history, nMin, nMax);
                resolved = lerp(history, current, alpha);
                historyWeight = 1.0 - alpha;
            }
            else
            {
                rejected = true;
            }
        }
        else
        {
            rejected = true;
        }
    }
    else if ((UseTaa != 0u) && currentHit)
    {
        rejected = true;
    }

    // Next-frame history stores the fog-free surface, so it keeps converging.
    HistoryXyzOut[ix] = float4(resolved, 1.0);
    HistoryHitOut[ix] = float4(currentHitPoint, 1.0);
    HistoryValidOut[ix] = currentHit ? 1u : 0u;

    // Per-pixel temporal signals for the Phase 5.2 reduction (AverageHistoryWeight,
    // RejectedHistoryPercent) — the resolve is where they are decided.
    HistoryWeightOut[ix] = historyWeight;
    RejectedOut[ix] = rejected ? 1u : 0u;

    // Debug-view switch (port of Phase5Reference.Colorize). DebugMode 0 = Beauty.
    float3 outColor;
    if (DebugMode == 1u) // SampleCount
    {
        outColor = PaletteSampleCount(SampleCountBuf[ix], SampleCountNorm);
    }
    else if (DebugMode == 2u) // Depth
    {
        outColor = currentHit ? PaletteDepth(length(currentHitPoint - CurrCamPos)) : float3(0.0, 0.0, 0.0);
    }
    else if (DebugMode == 3u) // Normal
    {
        outColor = PaletteNormal(NormalIn[ix].xyz);
    }
    else if (DebugMode == 4u) // Albedo
    {
        outColor = PaletteAlbedo(NormalIn[ix].w);
    }
    else if (DebugMode == 5u) // DirectLighting
    {
        outColor = ResolveToSRGB(DirectAccum[ix].xyz);
    }
    else if (DebugMode == 6u) // IndirectLighting
    {
        outColor = ResolveToSRGB(IndirectAccum[ix].xyz);
    }
    else if (DebugMode == 7u) // HistoryWeight
    {
        outColor = PaletteHistoryWeight(historyWeight);
    }
    else if (DebugMode == 8u) // RejectionMask
    {
        outColor = rejected ? float3(1.0, 0.1, 0.1) : float3(0.1, 0.9, 0.1);
    }
    else if (DebugMode == 9u) // Variance
    {
        uint sc = SampleCountBuf[ix];
        float variance = sc > 1u ? LumaM2[ix] / float(sc - 1u) : 0.0;
        outColor = PaletteVariance(variance, VarianceNorm);
    }
    else if (DebugMode == 10u) // ClampHeatmap
    {
        outColor = PaletteClamp(ClampAmountIn[ix]);
    }
    else // Beauty: composite this frame's fresh fog onto the resolved surface.
    {
        float4 fog = FogIn[ix];
        float3 withFog = resolved * fog.w + fog.xyz;
        outColor = ResolveToSRGB(withFog);
        // §1.4/§2.3 Classic depth cue: a deliberately non-physical, display-space darkening
        // that falls off exponentially with the primary hit distance, so the unlit fullbright
        // corridors fade toward dark down their length like the original screensaver. Opt-in
        // (ClassicDepthCue == 0 disables it) and Beauty-only, so the debug views and the
        // Enhanced/lit path are untouched. Background (no hit) keeps full brightness.
        if (ClassicDepthCue > 0.0 && currentHit)
            outColor *= exp(-ClassicDepthCue * length(currentHitPoint - CurrCamPos));
    }

    // The rat is now a first-class path-traced primary hit (see PathTracePhase6.ShadeSample), so it
    // is already in outColor — fogged, occluded, and reprojected like any surface. No screen-space
    // compositing here.
    outColor = ApplyMinimap(outColor, tid.xy);
    outColor *= (1.0 - FadeLevel); // §9.4 outro fade-to-black between mazes
    Output[tid.xy] = float4(saturate(outColor), 1.0);
}
