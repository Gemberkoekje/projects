// Phase 1 — fullbright spectral path tracer (DXR 1.1 inline ray tracing).
//
// One thread per pixel: generate a jittered primary ray, trace it against the
// hardware acceleration structure, and shade the hit with the maze's spectral
// materials — no lighting yet (LightingMode.None / Arc 1 fullbright). The result
// is folded into a per-pixel running-mean accumulation buffer that persists
// across frames, then resolved to sRGB and written to the output texture.
//
// This is a line-for-line port of RayTracer.Core's Phase1Reference.cs, which the
// unit tests pin to the CPU renderer. Requires Shader Model 6.5 (RayQuery).

#define DETERMINISTIC_COUNT 50u
#define COMPANION_COUNT     4u

// ── Bindings ──────────────────────────────────────────────────────────
// All buffers are bound as root descriptors; only the output texture needs a
// descriptor table (textures cannot be root descriptors).

struct PrimitiveInfo
{
    float L1X; float L1Y; float L1Z;
    float E1X; float E1Y; float E1Z;
    float E2X; float E2Y; float E2Z;
    float NX;  float NY;  float NZ;
    float InvEdge1LenSq;
    float InvEdge2LenSq;
    uint  Pattern;        // 0 plain, 1 brick, 2 ceiling tile
    uint  MatPrimary;
    uint  MatSecondary;
    float P0; float P1; float P2; float P3;
    float Pad0; float Pad1; float Pad2;
};

RaytracingAccelerationStructure Scene              : register(t0);
StructuredBuffer<PrimitiveInfo>  Primitives        : register(t1);
StructuredBuffer<float4>         DeterXYZ           : register(t2); // xyz used
StructuredBuffer<float>          MaterialReflectance: register(t3); // [material*50 + index]

RWStructuredBuffer<float4> Accum            : register(u0); // xyz running mean
RWStructuredBuffer<uint>   SampleCount      : register(u1);
RWStructuredBuffer<uint>   WavelengthCounter: register(u2);
RWTexture2D<float4>        Output           : register(u3);

cbuffer Constants : register(b0)
{
    float3 CamPos;                float _pad0;
    float4 CamRot;               // quaternion (x, y, z, w)
    float  TanHalfFov;           float AspectTanHalfFov; float InvWidth; float InvHeight;
    float  ImgPlaneZ;            float DeterministicCorrection; float _pad1; float _pad2;
    uint   Width;                uint  Height; uint MaxSampleCount; uint ResetFlag;
    uint   NumPrimitives;        uint  SubPixelJitter; uint _pad3; uint _pad4;
};

// ── Pure math (mirrors Phase1Reference.cs) ────────────────────────────

uint Hash2D(int x, int y)
{
    uint h = uint(x * 374761393 + y * 668265263);
    h = (h ^ (h >> 13)) * 1274126177u;
    return h ^ (h >> 16);
}

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

bool IsMortar(float u, float w, float bricksAcross, float bricksDown, float mortarU, float mortarV)
{
    float brickU = u * bricksAcross;
    float brickV = w * bricksDown;
    int row = (int)floor(brickV);
    float offsetU = brickU + (((row % 2) == 0) ? 0.0 : 0.5);
    float cellU = offsetU - floor(offsetU);
    float cellV = brickV - floor(brickV);
    return cellU < mortarU || cellU > (1.0 - mortarU)
        || cellV < mortarV || cellV > (1.0 - mortarV);
}

float BevelFactor(float u, float w, float tilesAcross, float tilesDown, float bevelFraction)
{
    float tileU = u * tilesAcross;
    float tileV = w * tilesDown;
    float cellU = tileU - floor(tileU);
    float cellV = tileV - floor(tileV);
    float distU = min(cellU, 1.0 - cellU);
    float distV = min(cellV, 1.0 - cellV);
    float edgeDist = min(distU, distV);
    if (edgeDist >= bevelFraction)
        return 1.0;
    float t = edgeDist / bevelFraction;
    float smoothT = t * t * (3.0 - 2.0 * t);
    return 0.4 + 0.6 * smoothT;
}

// Fullbright deterministic-corrected XYZ for one sample at a hit.
float3 ShadeHit(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, uint pixelHash, uint sampleIdx)
{
    float3 l1 = float3(prim.L1X, prim.L1Y, prim.L1Z);
    float3 e1 = float3(prim.E1X, prim.E1Y, prim.E1Z);
    float3 e2 = float3(prim.E2X, prim.E2Y, prim.E2Z);
    float3 nrm = float3(prim.NX, prim.NY, prim.NZ);

    float3 v = hitPoint - l1;
    float u = dot(v, e1) * prim.InvEdge1LenSq;
    float w = dot(v, e2) * prim.InvEdge2LenSq;

    float cosTheta = abs(dot(rayDir, nrm));

    uint row = prim.MatPrimary;
    float atten = 1.0;
    if (prim.Pattern == 1u) // brick
    {
        bool mortar = IsMortar(u, w, prim.P0, prim.P1, prim.P2, prim.P3);
        atten = 0.4 + 0.6 * cosTheta;
        if (mortar)
        {
            atten *= 0.6;
            row = prim.MatSecondary;
        }
    }
    else if (prim.Pattern == 2u) // ceiling tile
    {
        atten = (0.4 + 0.6 * cosTheta) * BevelFactor(u, w, prim.P0, prim.P1, prim.P2);
    }

    uint heroIdx = ((pixelHash % DETERMINISTIC_COUNT) + (sampleIdx % DETERMINISTIC_COUNT)) % DETERMINISTIC_COUNT;
    uint stride = DETERMINISTIC_COUNT / COMPANION_COUNT;
    uint reflBase = row * DETERMINISTIC_COUNT;

    float3 xyz = float3(0.0, 0.0, 0.0);
    [unroll]
    for (uint k = 0u; k < COMPANION_COUNT; k++)
    {
        uint idx = (heroIdx + k * stride) % DETERMINISTIC_COUNT;
        float refl = MaterialReflectance[reflBase + idx] * atten;
        xyz += DeterXYZ[idx].xyz * refl;
    }

    float3 baseXyz = xyz / float(COMPANION_COUNT);
    return baseXyz * DeterministicCorrection;
}

float LinearToSRGB(float linear)
{
    if (linear <= 0.0031308)
        return 12.92 * linear;
    return 1.055 * pow(linear, 1.0 / 2.4) - 0.055;
}

float3 ResolveToSRGB(float3 xyz)
{
    float lr = xyz.x * 3.2406 + xyz.y * (-1.5372) + xyz.z * (-0.4986);
    float lg = xyz.x * (-0.9689) + xyz.y * 1.8758 + xyz.z * 0.0415;
    float lb = xyz.x * 0.0557 + xyz.y * (-0.2040) + xyz.z * 1.0570;
    return float3(LinearToSRGB(lr), LinearToSRGB(lg), LinearToSRGB(lb));
}

// ── Kernel ────────────────────────────────────────────────────────────

[numthreads(8, 8, 1)]
void CSMain(uint3 tid : SV_DispatchThreadID)
{
    if (tid.x >= Width || tid.y >= Height)
        return;

    uint ix = tid.y * Width + tid.x;
    int x = int(tid.x);
    int y = int(tid.y);

    bool reset = (ResetFlag != 0u);
    uint prevCount = reset ? 0u : SampleCount[ix];
    uint sampleIdx = reset ? 0u : WavelengthCounter[ix];

    // Sub-pixel jitter (matches TraceCore's PCG-style seed chain).
    float jx, jy;
    if (SubPixelJitter != 0u)
    {
        uint baseHash = Hash2D(x, y);
        uint seed = baseHash + sampleIdx * 747796405u + 2891336453u;
        seed = seed * 747796405u + 2891336453u;
        jx = (seed & 0xFFFF) / 65536.0;
        seed = seed * 747796405u + 2891336453u;
        jy = (seed & 0xFFFF) / 65536.0;
    }
    else
    {
        jx = 0.5;
        jy = 0.5;
    }

    float px = (2.0 * ((x + jx) * InvWidth) - 1.0) * AspectTanHalfFov;
    float py = (1.0 - 2.0 * ((y + jy) * InvHeight)) * TanHalfFov;
    float3 localDir = float3(px, py, ImgPlaneZ);
    float3 dir = normalize(RotateByQuaternion(CamRot, localDir));

    uint pixelHash = Hash2D(x, y);

    RayDesc ray;
    ray.Origin = CamPos;
    ray.Direction = dir;
    ray.TMin = 1e-4;
    ray.TMax = 1e4;

    RayQuery<RAY_FLAG_NONE> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray);
    q.Proceed();

    float3 corrected = float3(0.0, 0.0, 0.0);
    if (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
    {
        // Two triangles per quad → quad index is the triangle index >> 1.
        uint primIndex = q.CommittedPrimitiveIndex() >> 1;
        float tHit = q.CommittedRayT();
        float3 hitPoint = ray.Origin + ray.Direction * tHit;
        corrected = ShadeHit(Primitives[primIndex], dir, hitPoint, pixelHash, sampleIdx);
    }

    // Running-mean accumulation (Welford-style mean; matches AccumXYZ update).
    uint count = min(prevCount + 1u, MaxSampleCount);
    float3 prevAccum = (reset || prevCount == 0u) ? float3(0.0, 0.0, 0.0) : Accum[ix].xyz;
    float3 newAccum = prevAccum + (corrected - prevAccum) / float(count);

    Accum[ix] = float4(newAccum, 1.0);
    SampleCount[ix] = count;
    WavelengthCounter[ix] = sampleIdx + 1u;

    float3 srgb = saturate(ResolveToSRGB(newAccum));
    Output[tid.xy] = float4(srgb, 1.0);
}
