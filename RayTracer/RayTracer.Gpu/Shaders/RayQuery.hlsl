// Phase 0 spike: DXR 1.1 inline ray tracing (RayQuery) in a compute shader.
//
// Traces one primary ray per pixel against a hardware-built acceleration
// structure containing two triangles (a quad). Hits are shaded by barycentrics;
// misses draw a background gradient. Requires Shader Model 6.5.

RaytracingAccelerationStructure Scene  : register(t0);
RWTexture2D<float4>             Output : register(u0);

cbuffer Constants : register(b0)
{
    uint Width;
    uint Height;
    uint FrameCounter;
    uint Mode;
};

[numthreads(8, 8, 1)]
void CSMain(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= Width || tid.y >= Height)
        return;

    float2 uv     = (float2(tid.xy) + 0.5) / float2(Width, Height);
    float  aspect = float(Width) / float(Height);

    // Pinhole camera at (0,0,-3) looking towards +Z.
    float2 ndc = uv * 2.0 - 1.0;
    ndc.y = -ndc.y;
    float3 origin = float3(0.0, 0.0, -3.0);
    float3 dir    = normalize(float3(ndc.x * aspect, ndc.y, 1.5));

    RayDesc ray;
    ray.Origin    = origin;
    ray.Direction = dir;
    ray.TMin      = 1e-3;
    ray.TMax      = 1e4;

    // Inline ray tracing: traverse on the RT cores, no shader tables.
    RayQuery<RAY_FLAG_NONE> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray);
    q.Proceed();

    float3 color;
    if (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
    {
        float2 bary = q.CommittedTriangleBarycentrics();
        float  b0   = 1.0 - bary.x - bary.y;
        // Hit: red channel is always >= 0.30 so the headless self-test can
        // separate hits from the background purely by the red value.
        color = float3(0.30 + 0.70 * bary.x, 0.30 + 0.70 * bary.y, 0.15 + 0.20 * b0);
    }
    else
    {
        // Miss: background gradient with red kept < 0.20.
        color = float3(0.02 + uv.x * 0.15, 0.05 + uv.y * 0.15, 0.35);
    }

    Output[tid.xy] = float4(color, 1.0);
}
