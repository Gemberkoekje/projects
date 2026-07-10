// Phase 2 — spectral path tracer with lighting (DXR 1.1 inline ray tracing).
//
// Extends the Phase 1 fullbright shader with next-event-estimation direct
// lighting (weighted light selection + shadow RayQuery) and a one-bounce +
// tertiary-bounce indirect estimator, exactly mirroring JobSystem.TraceCore's
// LightingMode.None / Direct / NEE branches with the diffuse irradiance cache
// dropped (see the plan's Phase 2.4 — the GPU re-traces the tertiary ray every
// sample instead).
//
// One thread per pixel; the per-sample result is folded into persistent
// running-mean accumulation buffers (total + direct + indirect), then resolved
// to sRGB. This is a line-for-line port of RayTracer.Core's Phase2Reference.cs,
// which the unit tests pin to the CPU renderer (JobSystem.TraceCore). Requires
// Shader Model 6.5 (RayQuery).

#define DETERMINISTIC_COUNT 50u
#define COMPANION_COUNT     4u
#define PI                  3.14159265
#define RNG_MUL             747796405u
#define RNG_ADD             2891336453u

// ── Bindings ──────────────────────────────────────────────────────────

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
StructuredBuffer<float4>         Lights             : register(t4); // xyz = world position

RWStructuredBuffer<float4> Accum            : register(u0); // xyz running mean (total)
RWStructuredBuffer<uint>   SampleCount      : register(u1);
RWStructuredBuffer<uint>   WavelengthCounter: register(u2);
RWTexture2D<float4>        Output           : register(u3);
RWStructuredBuffer<uint>   LastHit          : register(u4); // 1 = pixel hit geometry last sample
RWStructuredBuffer<float4> DirectAccum      : register(u5); // xyz running mean (direct)
RWStructuredBuffer<float4> IndirectAccum    : register(u6); // xyz running mean (indirect)

cbuffer Constants : register(b0)
{
    float3 CamPos;                float _pad0;
    float4 CamRot;               // quaternion (x, y, z, w)
    float  TanHalfFov;           float AspectTanHalfFov; float InvWidth; float InvHeight;
    float  ImgPlaneZ;            float DeterministicCorrection; float AmbientLevel; float LightIntensity;
    uint   Width;                uint  Height; uint MaxSampleCount; uint ResetFlag;
    uint   NumPrimitives;        uint  SubPixelJitter; uint NumLights; uint LightingMode; // 0 none, 1 direct, 2 NEE
    float  SampleClamp;          float _pad1; float _pad2; float _pad3;
};

// ── Pure math (mirrors Phase1Reference.cs / Phase2Reference.cs) ────────

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

// Effective reflectance row + wavelength-independent pattern attenuation.
void PatternShade(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, out uint row, out float atten)
{
    float3 l1 = float3(prim.L1X, prim.L1Y, prim.L1Z);
    float3 e1 = float3(prim.E1X, prim.E1Y, prim.E1Z);
    float3 e2 = float3(prim.E2X, prim.E2Y, prim.E2Z);
    float3 nrm = float3(prim.NX, prim.NY, prim.NZ);

    float3 v = hitPoint - l1;
    float u = dot(v, e1) * prim.InvEdge1LenSq;
    float w = dot(v, e2) * prim.InvEdge2LenSq;

    float cosTheta = abs(dot(rayDir, nrm));

    row = prim.MatPrimary;
    atten = 1.0;
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
}

// Face normal flipped to oppose the ray (denom < 0 ? Normal : -Normal).
float3 FaceNormal(PrimitiveInfo prim, float3 rayDir)
{
    float3 n = float3(prim.NX, prim.NY, prim.NZ);
    return (dot(rayDir, n) < 0.0) ? n : -n;
}

// Averaged hero + 3-companion diffuse albedo XYZ (no correction).
float3 BaseXyz(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, uint pixelHash, uint sampleIdx)
{
    uint row; float atten;
    PatternShade(prim, rayDir, hitPoint, row, atten);

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
    return xyz / float(COMPANION_COUNT);
}

// Single-hero-wavelength diffuse albedo XYZ (for indirect bounces).
float3 IndirectBaseXyz(PrimitiveInfo prim, float3 rayDir, float3 hitPoint, uint heroIdx)
{
    uint row; float atten;
    PatternShade(prim, rayDir, hitPoint, row, atten);
    float refl = MaterialReflectance[row * DETERMINISTIC_COUNT + heroIdx] * atten;
    return DeterXYZ[heroIdx].xyz * refl;
}

float3 CosineHemisphere(float3 n, float r1, float r2)
{
    float sqrtR1 = sqrt(r1);
    float theta = 2.0 * PI * r2;
    float sx = sqrtR1 * cos(theta);
    float sy = sqrtR1 * sin(theta);
    float sz = sqrt(max(0.0, 1.0 - r1));

    float3 tangent = (abs(n.x) > 0.1)
        ? normalize(float3(n.y, -n.x, 0.0))
        : normalize(float3(0.0, n.z, -n.y));
    float3 bitangent = cross(n, tangent);
    return normalize(sx * tangent + sy * bitangent + sz * n);
}

// ── Ray casting (RayQuery) ────────────────────────────────────────────

bool TraceClosest(float3 origin, float3 dir, out uint quadIndex, out float3 hitPoint)
{
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = 1e-4;
    ray.TMax = 1e4;

    RayQuery<RAY_FLAG_NONE> q;
    q.TraceRayInline(Scene, RAY_FLAG_NONE, 0xFF, ray);
    q.Proceed();

    if (q.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
    {
        // Two triangles per quad → quad index is the triangle index >> 1.
        quadIndex = q.CommittedPrimitiveIndex() >> 1;
        float tHit = q.CommittedRayT();
        hitPoint = origin + dir * tHit;
        return true;
    }
    quadIndex = 0u;
    hitPoint = float3(0.0, 0.0, 0.0);
    return false;
}

bool TraceOccluded(float3 origin, float3 dir, float maxDist)
{
    // No valid interval → nothing occludes (matches BVH.IsOccluded, whose hits
    // require t >= 1e-4 and t < maxDist).
    if (maxDist <= 1e-4)
        return false;

    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = dir;
    ray.TMin = 1e-4;
    ray.TMax = maxDist;

    RayQuery<RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> q;
    q.TraceRayInline(Scene, RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH, 0xFF, ray);
    q.Proceed();
    return q.CommittedStatus() == COMMITTED_TRIANGLE_HIT;
}

// ── Lighting (mirrors Phase2Reference) ────────────────────────────────

float LightWeight(float3 lightPos, float3 samplePoint, float3 normal)
{
    float3 toLight = lightPos - samplePoint;
    float dsq = dot(toLight, toLight);
    float dist = sqrt(dsq);
    float3 ldir = toLight / dist;
    float cosv = dot(normal, ldir);
    return cosv > 0.0 ? 1.0 / max(dsq, 1e-6) : 0.0;
}

void FillLight(float3 lightPos, float3 samplePoint, float3 normal, out float3 outDir, out float outDistSq, out float outCos)
{
    float3 toLight = lightPos - samplePoint;
    outDistSq = max(dot(toLight, toLight), 1e-6);
    float dist = sqrt(outDistSq);
    outDir = toLight / dist;
    outCos = dot(normal, outDir);
}

// Weighted light importance sampling. Weights are recomputed (not cached) so no
// per-thread MAX_LIGHTS array is needed; recomputation is bit-identical to the
// CPU's cached array, so the pick and pdf match exactly.
void SelectLight(inout uint rng, float3 samplePoint, float3 normal,
                 out float outP, out float3 outDir, out float outDistSq, out float outCos)
{
    float totalW = 0.0;
    for (uint li = 0u; li < NumLights; li++)
        totalW += LightWeight(Lights[li].xyz, samplePoint, normal);

    rng = rng * RNG_MUL + RNG_ADD;

    if (totalW > 0.0)
    {
        float u = float(rng) / 4294967296.0 * totalW;
        float acc = 0.0;
        for (uint li = 0u; li < NumLights; li++)
        {
            float w = LightWeight(Lights[li].xyz, samplePoint, normal);
            acc += w;
            if (u <= acc)
            {
                FillLight(Lights[li].xyz, samplePoint, normal, outDir, outDistSq, outCos);
                outP = w / totalW;
                return;
            }
        }
        uint last = NumLights - 1u;
        FillLight(Lights[last].xyz, samplePoint, normal, outDir, outDistSq, outCos);
        outP = LightWeight(Lights[last].xyz, samplePoint, normal) / totalW;
        return;
    }

    uint idx = rng % NumLights;
    FillLight(Lights[idx].xyz, samplePoint, normal, outDir, outDistSq, outCos);
    outP = 1.0 / float(NumLights);
}

// Uniform-selection NEE direct term at an indirect bounce.
float UniformLightTerm(uint lightIdx, float3 hitPoint, float3 hitNormal)
{
    float3 toLight = Lights[lightIdx].xyz - hitPoint;
    float distSq = dot(toLight, toLight);
    float dist = sqrt(distSq);
    float3 lightDir = toLight / dist;
    float cosTheta = dot(hitNormal, lightDir);
    if (cosTheta <= 0.0)
        return 0.0;

    float3 shadowOrigin = hitPoint + hitNormal * 1e-3;
    if (TraceOccluded(shadowOrigin, lightDir, dist - 2e-3))
        return 0.0;

    return cosTheta / distSq * LightIntensity * float(NumLights);
}

// Full per-sample corrected XYZ; also returns the direct/indirect components and
// whether the primary ray hit geometry.
float3 ShadeSample(float3 camPos, float3 primaryDir, uint pixelHash, uint sampleIdx,
                   out bool primaryHit, out float3 correctedDirect, out float3 correctedIndirect)
{
    correctedDirect = float3(0.0, 0.0, 0.0);
    correctedIndirect = float3(0.0, 0.0, 0.0);

    uint primIndex;
    float3 hitPoint;
    primaryHit = TraceClosest(camPos, primaryDir, primIndex, hitPoint);
    if (!primaryHit)
        return float3(0.0, 0.0, 0.0);

    PrimitiveInfo prim = Primitives[primIndex];
    float3 hitNormal = FaceNormal(prim, primaryDir);

    uint heroIdx = ((pixelHash % DETERMINISTIC_COUNT) + (sampleIdx % DETERMINISTIC_COUNT)) % DETERMINISTIC_COUNT;
    float3 baseXyz = BaseXyz(prim, primaryDir, hitPoint, pixelHash, sampleIdx);

    bool lit = (LightingMode != 0u) && (NumLights > 0u);

    float3 xyz;
    // TraceCore only commits the direct AOV (bounce0) inside the indirect-hit
    // block; on a secondary miss it stays zero even though directTerm is already
    // folded into the total. Mirror that exactly (start at zero).
    float3 bounce0 = float3(0.0, 0.0, 0.0);
    float3 indirect = float3(0.0, 0.0, 0.0);

    if (!lit)
    {
        xyz = baseXyz;
        bounce0 = baseXyz;
    }
    else
    {
        float ambientTerm = AmbientLevel;

        // ── Direct lighting (NEE) ──────────────────────────────────────
        float directTerm = 0.0;
        uint rngLight = pixelHash + sampleIdx * RNG_MUL + RNG_ADD;
        float lightP; float3 lightDir; float lightDistSq; float lightCos;
        SelectLight(rngLight, hitPoint, hitNormal, lightP, lightDir, lightDistSq, lightCos);

        if (lightCos > 0.0)
        {
            bool visible = true;
            if (LightingMode == 2u) // NEE
            {
                float3 shadowOrigin = hitPoint + hitNormal * 1e-3;
                visible = !TraceOccluded(shadowOrigin, lightDir, sqrt(lightDistSq) - 2e-3);
            }
            if (visible)
                directTerm += lightCos / lightDistSq * LightIntensity / max(lightP, 1e-9);
        }

        xyz = baseXyz * (ambientTerm + directTerm);

        // ── Indirect: one bounce + tertiary bounce ─────────────────────
        uint rng = pixelHash + sampleIdx * RNG_MUL + RNG_ADD;
        rng = rng * RNG_MUL + RNG_ADD;
        float r1 = (rng & 0xFFFF) / 65536.0;
        rng = rng * RNG_MUL + RNG_ADD;
        float r2 = (rng & 0xFFFF) / 65536.0;

        float3 sampleDir = CosineHemisphere(hitNormal, r1, r2);
        float3 secOrigin = hitPoint + hitNormal * 1e-3;

        uint secIndex;
        float3 secHitPoint;
        if (TraceClosest(secOrigin, sampleDir, secIndex, secHitPoint))
        {
            PrimitiveInfo secPrim = Primitives[secIndex];
            float3 secHitNormal = FaceNormal(secPrim, sampleDir);
            float3 secBaseXyz = IndirectBaseXyz(secPrim, sampleDir, secHitPoint, heroIdx);

            float secDirectTerm = 0.0;
            rng = rng * RNG_MUL + RNG_ADD;
            uint lightIdx2 = rng % NumLights;
            secDirectTerm += UniformLightTerm(lightIdx2, secHitPoint, secHitNormal);

            float3 secIncoming = secBaseXyz * (AmbientLevel + secDirectTerm);

            float3 secBounce2Plus = float3(0.0, 0.0, 0.0);
            rng = rng * RNG_MUL + RNG_ADD;
            float r3 = (rng & 0xFFFF) / 65536.0;
            rng = rng * RNG_MUL + RNG_ADD;
            float r4 = (rng & 0xFFFF) / 65536.0;

            float3 tertDir = CosineHemisphere(secHitNormal, r3, r4);
            float3 tertOrigin = secHitPoint + secHitNormal * 1e-3;

            uint tertIndex;
            float3 tertHitPoint;
            if (TraceClosest(tertOrigin, tertDir, tertIndex, tertHitPoint))
            {
                PrimitiveInfo tertPrim = Primitives[tertIndex];
                float3 tertHitNormal = FaceNormal(tertPrim, tertDir);
                float3 tertBaseXyz = IndirectBaseXyz(tertPrim, tertDir, tertHitPoint, heroIdx);

                float tertDirectTerm = 0.0;
                rng = rng * RNG_MUL + RNG_ADD;
                uint lightIdx3 = rng % NumLights;
                tertDirectTerm += UniformLightTerm(lightIdx3, tertHitPoint, tertHitNormal);

                float3 tertIncoming = tertBaseXyz * (AmbientLevel + tertDirectTerm);
                secBounce2Plus = secBaseXyz * tertIncoming;
            }

            bounce0 = baseXyz * directTerm;
            float3 localBounce1 = baseXyz * secIncoming;
            float3 localBounce2Plus = baseXyz * secBounce2Plus;
            indirect = localBounce1 + localBounce2Plus;
            xyz += indirect;
        }
    }

    float correction = DeterministicCorrection;
    correctedDirect = bounce0 * correction;
    correctedIndirect = indirect * correction;
    return xyz * correction;
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
    uint sampleIdx = reset ? 0u : WavelengthCounter[ix];

    // Sub-pixel jitter (matches TraceCore's PCG-style seed chain).
    float jx, jy;
    if (SubPixelJitter != 0u)
    {
        uint baseHash = Hash2D(x, y);
        uint seed = baseHash + sampleIdx * RNG_MUL + RNG_ADD;
        seed = seed * RNG_MUL + RNG_ADD;
        jx = (seed & 0xFFFF) / 65536.0;
        seed = seed * RNG_MUL + RNG_ADD;
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

    bool hit;
    float3 correctedDirect, correctedIndirect;
    float3 corrected = ShadeSample(CamPos, dir, pixelHash, sampleIdx, hit, correctedDirect, correctedIndirect);

    // Firefly clamp on the total only (matches TraceCore's SampleClamp, which
    // leaves the direct/indirect debug channels unclamped).
    if (SampleClamp > 0.0)
        corrected = clamp(corrected, float3(0.0, 0.0, 0.0), float3(SampleClamp, SampleClamp, SampleClamp));

    // Running-mean accumulation. Restart the mean on a whole-frame reset, or when
    // this pixel flips between hitting geometry and missing (matches TraceCore's
    // 'hit != LastHit' reset). WavelengthCounter keeps advancing regardless.
    bool restart = reset || (LastHit[ix] != (hit ? 1u : 0u));
    uint prevCount = restart ? 0u : SampleCount[ix];
    uint count = min(prevCount + 1u, MaxSampleCount);
    bool clearMean = restart || (prevCount == 0u);

    float3 prevAccum = clearMean ? float3(0.0, 0.0, 0.0) : Accum[ix].xyz;
    float3 newAccum = prevAccum + (corrected - prevAccum) / float(count);

    float3 prevDirect = clearMean ? float3(0.0, 0.0, 0.0) : DirectAccum[ix].xyz;
    float3 newDirect = prevDirect + (correctedDirect - prevDirect) / float(count);

    float3 prevIndirect = clearMean ? float3(0.0, 0.0, 0.0) : IndirectAccum[ix].xyz;
    float3 newIndirect = prevIndirect + (correctedIndirect - prevIndirect) / float(count);

    Accum[ix] = float4(newAccum, 1.0);
    DirectAccum[ix] = float4(newDirect, 1.0);
    IndirectAccum[ix] = float4(newIndirect, 1.0);
    SampleCount[ix] = count;
    WavelengthCounter[ix] = sampleIdx + 1u;
    LastHit[ix] = hit ? 1u : 0u;

    float3 srgb = saturate(ResolveToSRGB(newAccum));
    Output[tid.xy] = float4(srgb, 1.0);
}
