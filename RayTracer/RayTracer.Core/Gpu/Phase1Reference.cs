using System.Numerics;

namespace RayTracer;

/// <summary>
/// A CPU replica of the Phase 1 fullbright GPU shader's pure math: ray
/// generation, sub-pixel jitter, the brick/ceiling pattern functions, and the
/// hero + companion spectral resolve. <c>PathTrace.hlsl</c> is a line-for-line
/// port of the methods here, and the unit tests pin this replica to the CPU
/// renderer's own classes (<see cref="WavelengthLookup"/>, the rectangle
/// <c>Intersect</c> methods). That makes shader/CPU spectral parity testable in
/// plain C#, where the GPU cannot run.
///
/// This mirrors the <c>LightingMode.None</c> (Arc 1 fullbright) branch of
/// <c>JobSystem.TraceCore</c>.
/// </summary>
public static class Phase1Reference
{
    /// <summary>Wavelengths evaluated per hit (hero + 3 companions).</summary>
    public const int CompanionCount = 4;

    /// <summary>Per-pixel hash used to seed jitter and pick the hero wavelength.
    /// Identical to <c>JobSystem.Hash2D</c>.</summary>
    public static uint Hash2D(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    /// <summary>
    /// Rotates <paramref name="v"/> by quaternion <paramref name="q"/> using the
    /// exact expansion <see cref="Vector3.Transform(Vector3, Quaternion)"/> uses,
    /// so the ported HLSL matches the CPU camera to the low bits.
    /// </summary>
    public static Vector3 RotateByQuaternion(Vector4 q, Vector3 v)
    {
        float x2 = q.X + q.X;
        float y2 = q.Y + q.Y;
        float z2 = q.Z + q.Z;
        float wx2 = q.W * x2;
        float wy2 = q.W * y2;
        float wz2 = q.W * z2;
        float xx2 = q.X * x2;
        float xy2 = q.X * y2;
        float xz2 = q.X * z2;
        float yy2 = q.Y * y2;
        float yz2 = q.Y * z2;
        float zz2 = q.Z * z2;

        return new Vector3(
            v.X * (1f - yy2 - zz2) + v.Y * (xy2 - wz2) + v.Z * (xz2 + wy2),
            v.X * (xy2 + wz2) + v.Y * (1f - xx2 - zz2) + v.Z * (yz2 - wx2),
            v.X * (xz2 - wy2) + v.Y * (yz2 + wx2) + v.Z * (1f - xx2 - yy2));
    }

    /// <summary>
    /// Generates the primary ray direction for pixel (<paramref name="x"/>,
    /// <paramref name="y"/>) with sample index <paramref name="sampleIdx"/>,
    /// mirroring the ray-gen block of <c>TraceCore</c>.
    /// </summary>
    public static Vector3 PrimaryRayDirection(
        int x, int y, uint sampleIdx,
        Vector4 rotation, float imgPlaneZ,
        float tanHalfFov, float aspectTanHalfFov,
        float invWidth, float invHeight,
        bool subPixelJitter)
    {
        float jx, jy;
        if (subPixelJitter)
        {
            uint baseHash = Hash2D(x, y);
            uint seed = baseHash + sampleIdx * 747796405u + 2891336453u;
            seed = seed * 747796405u + 2891336453u;
            jx = (seed & 0xFFFF) / 65536f;
            seed = seed * 747796405u + 2891336453u;
            jy = (seed & 0xFFFF) / 65536f;
        }
        else
        {
            jx = 0.5f;
            jy = 0.5f;
        }

        float px = (2f * ((x + jx) * invWidth) - 1f) * aspectTanHalfFov;
        float py = (1f - 2f * ((y + jy) * invHeight)) * tanHalfFov;
        Vector3 localDir = new(px, py, imgPlaneZ);
        return Vector3.Normalize(RotateByQuaternion(rotation, localDir));
    }

    /// <summary>Deterministic hero index for this pixel/sample (matches
    /// <c>(pixelHash + sampleIdx) % DeterministicCount</c> computed in 64-bit).</summary>
    public static uint HeroIndex(uint pixelHash, uint sampleIdx, int deterministicCount)
    {
        uint n = (uint)deterministicCount;
        return ((pixelHash % n) + (sampleIdx % n)) % n;
    }

    /// <summary>
    /// Running-bond mortar test, identical to <c>BrickRectangle.IsMortar</c>.
    /// </summary>
    public static bool IsMortar(float u, float w, float bricksAcross, float bricksDown, float mortarFractionU, float mortarFractionV)
    {
        float brickU = u * bricksAcross;
        float brickV = w * bricksDown;
        int row = (int)MathF.Floor(brickV);
        float offsetU = brickU + (row % 2 == 0 ? 0f : 0.5f);
        float cellU = offsetU - MathF.Floor(offsetU);
        float cellV = brickV - MathF.Floor(brickV);
        return cellU < mortarFractionU || cellU > (1f - mortarFractionU)
            || cellV < mortarFractionV || cellV > (1f - mortarFractionV);
    }

    /// <summary>Ceiling-tile bevel factor, identical to
    /// <c>CeilingTileRectangle.BevelFactor</c>.</summary>
    public static float BevelFactor(float u, float w, float tilesAcross, float tilesDown, float bevelFraction)
    {
        float tileU = u * tilesAcross;
        float tileV = w * tilesDown;
        float cellU = tileU - MathF.Floor(tileU);
        float cellV = tileV - MathF.Floor(tileV);
        float distU = MathF.Min(cellU, 1f - cellU);
        float distV = MathF.Min(cellV, 1f - cellV);
        float edgeDist = MathF.Min(distU, distV);
        if (edgeDist >= bevelFraction)
            return 1f;
        float t = edgeDist / bevelFraction;
        float smooth = t * t * (3f - 2f * t);
        return 0.4f + 0.6f * smooth;
    }

    /// <summary>
    /// Resolves a quad hit's effective reflectance <paramref name="row"/> (the
    /// material row to sample — primary, or secondary/mortar where the brick
    /// pattern selects it) and per-hit <paramref name="atten"/> (the
    /// wavelength-independent pattern attenuation). Mirrors the pattern branches
    /// of the rectangle <c>Intersect</c> methods; the reflectance at any
    /// wavelength is then <c>MaterialReflectance[row*N + index] * atten</c>.
    /// </summary>
    public static void PatternShade(
        in GpuPrimitive prim, Vector3 rayDir, Vector3 hitPoint,
        out uint row, out float atten)
    {
        Vector3 l1 = new(prim.L1X, prim.L1Y, prim.L1Z);
        Vector3 e1 = new(prim.E1X, prim.E1Y, prim.E1Z);
        Vector3 e2 = new(prim.E2X, prim.E2Y, prim.E2Z);
        Vector3 n = new(prim.NX, prim.NY, prim.NZ);

        Vector3 v = hitPoint - l1;
        float u = Vector3.Dot(v, e1) * prim.InvEdge1LenSq;
        float w = Vector3.Dot(v, e2) * prim.InvEdge2LenSq;

        float cosTheta = MathF.Abs(Vector3.Dot(rayDir, n));

        row = prim.MatPrimary;
        atten = 1f;
        switch ((QuadPattern)prim.Pattern)
        {
            case QuadPattern.Brick:
            {
                bool mortar = IsMortar(u, w, prim.P0, prim.P1, prim.P2, prim.P3);
                atten = 0.4f + 0.6f * cosTheta;
                if (mortar)
                {
                    atten *= 0.6f;
                    row = prim.MatSecondary;
                }
                break;
            }
            case QuadPattern.CeilingTile:
            {
                atten = (0.4f + 0.6f * cosTheta) * BevelFactor(u, w, prim.P0, prim.P1, prim.P2);
                break;
            }
            case QuadPattern.Plain:
            default:
                atten = 1f;
                break;
        }
    }

    /// <summary>
    /// The averaged hero + 3-companion spectral XYZ at a hit, <b>without</b> the
    /// deterministic correction (the raw diffuse albedo colour). This is the
    /// <c>baseXyz</c> that <c>TraceCore</c> multiplies by the lighting terms
    /// before correcting; Phase 2 lighting builds on it.
    /// </summary>
    public static Vector3 BaseXyz(
        in GpuPrimitive prim, SpectralResources res,
        Vector3 rayDir, Vector3 hitPoint, uint pixelHash, uint sampleIdx)
    {
        PatternShade(prim, rayDir, hitPoint, out uint row, out float atten);

        int n50 = res.DeterministicCount;
        uint heroIdx = HeroIndex(pixelHash, sampleIdx, n50);
        uint stride = (uint)(n50 / CompanionCount);
        int reflBase = (int)row * n50;

        Vector3 xyz = Vector3.Zero;
        for (uint k = 0; k < CompanionCount; k++)
        {
            uint idx = (heroIdx + k * stride) % (uint)n50;
            float refl = res.MaterialReflectance[reflBase + (int)idx] * atten;
            var cie = new Vector3(res.DeterXYZ[idx * 4 + 0], res.DeterXYZ[idx * 4 + 1], res.DeterXYZ[idx * 4 + 2]);
            xyz += cie * refl;
        }

        return xyz / CompanionCount;
    }

    /// <summary>
    /// Resolves the fullbright, deterministic-corrected XYZ for one sample at a
    /// hit on <paramref name="prim"/>, using only the baked
    /// <see cref="SpectralResources"/>. This is the exact per-sample value the
    /// GPU accumulation buffer folds into its running mean, and it mirrors the
    /// <c>Lighting == LightingMode.None</c> resolve in <c>TraceCore</c>.
    /// </summary>
    /// <param name="prim">The hit quad.</param>
    /// <param name="res">Baked spectral tables.</param>
    /// <param name="rayDir">Normalized primary ray direction.</param>
    /// <param name="hitPoint">World-space hit position.</param>
    /// <param name="pixelHash">Per-pixel hash (<see cref="Hash2D"/>).</param>
    /// <param name="sampleIdx">Sample index for this pixel.</param>
    public static Vector3 ShadeHit(
        in GpuPrimitive prim, SpectralResources res,
        Vector3 rayDir, Vector3 hitPoint, uint pixelHash, uint sampleIdx)
        => BaseXyz(prim, res, rayDir, hitPoint, pixelHash, sampleIdx) * res.DeterministicCorrection;

    /// <summary>
    /// Converts an accumulated XYZ colour to a display sRGB triplet in [0,1],
    /// identical to <c>JobSystem.ToSRGB</c> followed by
    /// <c>DisplayResolver.LinearToSRGB</c>. Returned as (R, G, B).
    /// </summary>
    public static Vector3 ResolveToSRGB(Vector3 xyz)
    {
        // Row-vector times the sRGB matrix, matching JobSystem.TosRGBMatrix.
        float lr = xyz.X * 3.2406f + xyz.Y * -1.5372f + xyz.Z * -0.4986f;
        float lg = xyz.X * -0.9689f + xyz.Y * 1.8758f + xyz.Z * 0.0415f;
        float lb = xyz.X * 0.0557f + xyz.Y * -0.2040f + xyz.Z * 1.0570f;
        return new Vector3(LinearToSRGB(lr), LinearToSRGB(lg), LinearToSRGB(lb));
    }

    private static float LinearToSRGB(float linear)
    {
        if (linear <= 0.0031308f)
            return 12.92f * linear;
        return 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
    }
}
