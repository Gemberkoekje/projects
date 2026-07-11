// Phase 4 — resolve pass of the temporal pipeline, with volumetric compositing.
//
// Identical to ResolvePhase3.hlsl (spatial filter + TAA reprojection/blend of the
// converged *surface*), with one addition: after resolving the surface it
// composites the per-pixel volumetric fog the trace pass wrote fresh this frame
// (FogIn: xyz = inscatter*correction, w = transmittance) as `surface*T + inscatter`
// before the sRGB conversion. The temporal history stores the fog-free surface, so
// the surface keeps converging while the fog — deterministic and recomputed each
// frame at VolTime — animates cleanly on top without adding noise.
//
// The surface temporal math is the same line-for-line port of Phase3Reference.cs
// the Phase 3 resolve uses; only the final composite differs. Requires SM 6.0+.

#define BILATERAL_SHARPNESS 25.0
#define MIN_VALID_WEIGHT    0.25

// ── Bindings ──────────────────────────────────────────────────────────

RWStructuredBuffer<float4> Accum          : register(u0); // xyz accumulated mean (read)
RWStructuredBuffer<uint>   LastHit        : register(u4); // 1 = pixel hit (read)
RWStructuredBuffer<float4> HitPointIn     : register(u7); // xyz world hit point (read)
RWStructuredBuffer<float4> NormalIn       : register(u8); // xyz oriented normal (read)
RWStructuredBuffer<float4> FogIn          : register(u9); // xyz inscatter*correction, w transmittance (read)

RWStructuredBuffer<float4> HistoryXyzIn   : register(u10); // previous resolved (read)
RWStructuredBuffer<float4> HistoryHitIn   : register(u11); // previous hit point (read)
RWStructuredBuffer<uint>   HistoryValidIn : register(u12); // previous hit mask (read)

RWStructuredBuffer<float4> HistoryXyzOut  : register(u13); // next resolved (write)
RWStructuredBuffer<float4> HistoryHitOut  : register(u14); // next hit point (write)
RWStructuredBuffer<uint>   HistoryValidOut: register(u15); // next hit mask (write)

RWTexture2D<float4>        Output         : register(u3); // resolved sRGB image (write)

cbuffer ResolveConstants : register(b0)
{
    float3 PrevCamPos;   float _rpad0;
    float4 PrevCamRot;   // previous-frame camera quaternion (x, y, z, w)
    float  TanHalfFov;   float AspectTanHalfFov; float TemporalBlendAlpha; float ReprojThreshold;
    uint   Width;        uint  Height; uint UseTaa; uint IsMoving;
    uint   FilterRadius; uint  EdgeAware; uint _rpad1; uint _rpad2;
};

// ── Spatial (bilateral) filter — mirrors JobSystem.ResolveFilteredXYZ ──

float3 FilteredXYZ(int x, int y, int radius)
{
    if (radius <= 0)
        return Accum[y * int(Width) + x].xyz;

    int yMin = max(y - radius, 0);
    int yMax = min(y + radius, int(Height) - 1);
    int xMin = max(x - radius, 0);
    int xMax = min(x + radius, int(Width) - 1);

    if (EdgeAware != 0u)
    {
        float3 centerXyz = Accum[y * int(Width) + x].xyz;
        float3 xyz = float3(0.0, 0.0, 0.0);
        float totalWeight = 0.0;
        for (int ny = yMin; ny <= yMax; ny++)
        {
            int rowOff = ny * int(Width);
            for (int nx = xMin; nx <= xMax; nx++)
            {
                float3 neighbor = Accum[rowOff + nx].xyz;
                float lumDiff = centerXyz.y - neighbor.y;
                float w = exp(-lumDiff * lumDiff * BILATERAL_SHARPNESS);
                xyz += neighbor * w;
                totalWeight += w;
            }
        }
        return totalWeight > 0.0 ? xyz / totalWeight : centerXyz;
    }

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
    // Inverse of a unit quaternion is its conjugate.
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

// ── Kernel ────────────────────────────────────────────────────────────

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
            }
        }
    }

    // Next-frame history stores the fog-free surface, so it keeps converging.
    HistoryXyzOut[ix] = float4(resolved, 1.0);
    HistoryHitOut[ix] = float4(currentHitPoint, 1.0);
    HistoryValidOut[ix] = currentHit ? 1u : 0u;

    // Composite this frame's fresh fog onto the resolved surface for display only.
    float4 fog = FogIn[ix];
    float3 withFog = resolved * fog.w + fog.xyz;

    float3 srgb = saturate(ResolveToSRGB(withFog));
    Output[tid.xy] = float4(srgb, 1.0);
}
