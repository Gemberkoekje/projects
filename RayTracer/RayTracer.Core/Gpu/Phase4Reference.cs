using System;
using System.Numerics;

namespace RayTracer;

/// <summary>
/// A CPU replica of the Phase 4 (volumetrics) GPU shader's pure math: the
/// smoke/fog density fields and the single-scattering camera-segment integrator
/// (transmittance + in-scatter). <c>PathTracePhase4.hlsl</c> folds a line-for-line
/// port of the methods here into its <c>ShadeSample</c>, and the unit tests pin
/// this replica to the renderer's own <c>JobSystem.IntegrateVolumetricSegment</c>
/// (over the real <see cref="BVH"/> + <see cref="Light"/> array). That makes
/// shader/CPU volumetric parity testable in plain C#, where the GPU cannot run.
///
/// The density functions mirror the private <c>JobSystem.GetDensity*</c> methods
/// in <c>PathTracer.cs</c>; the marcher mirrors <c>IntegrateVolumetricSegment</c>
/// in <c>VolumetricIntegration.cs</c>. Every operation is replicated in the same
/// order with the same <see cref="MathF"/> primitives so the C# side matches by
/// construction; the HLSL port is what the dev-box self-test validates on hardware.
///
/// <para><b>Compositing.</b> The renderer applies the result to the <em>raw</em>
/// (pre-<c>DeterministicCorrection</c>) surface XYZ as <c>xyz * T + Inscatter</c>.
/// Because the correction is a scalar, the corrected result equals
/// <c>correctedXyz * T + Inscatter * correction</c> — see <see cref="Apply"/>.</para>
/// </summary>
public static class Phase4Reference
{
    /// <summary>Normalized isotropic phase value <c>1/(4π)</c>
    /// (mirrors <c>JobSystem.IsotropicPhase</c>).</summary>
    public const float IsotropicPhase = 0.07957747f;

    /// <summary>Grey medium tint applied to in-scatter and ambient
    /// (mirrors <c>JobSystem.SmokeTint</c>).</summary>
    public static readonly Vector3 SmokeTint = new(0.97f, 0.97f, 0.97f);

    /// <summary>Point-light intensity (mirrors <c>JobSystem.LightIntensity</c>).</summary>
    public const float LightIntensity = Phase2Reference.LightIntensity;

    /// <summary>Base ambient level (mirrors <c>JobSystem.AmbientLevel</c>).</summary>
    public const float AmbientLevel = Phase2Reference.AmbientLevel;

    /// <summary>Biome tile size in cells (mirrors <c>GetDensityBiome</c>).</summary>
    public const int BiomeSizeCells = 4;

    // ── Density field (mirrors JobSystem.GetDensity* in PathTracer.cs) ─────

    private static uint HashCell(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h ^= h >> 16;
        h *= 2246822519u;
        h ^= h >> 13;
        h *= 3266489917u;
        return h ^ (h >> 16);
    }

    private static bool IsSmokeBiome(int biomeX, int biomeY)
        => (biomeX + biomeY) % 3 == 1;

    private static bool IsFogBiome(int biomeX, int biomeY)
    {
        uint biomeHash = HashCell(biomeX, biomeY);
        return (biomeHash & 1u) == 0u;
    }

    private static float SmokeCoverage(float x, float z)
    {
        float bands = 0.5f + 0.5f * MathF.Sin(x * 0.37f + z * 0.53f);
        float swirl = 0.5f + 0.5f * MathF.Sin(x * 0.19f - z * 0.29f + bands * 3.1f);
        return 0.35f + 0.65f * (0.6f * bands + 0.4f * swirl);
    }

    // Non-turbulence density profiles shared by the Always* modes and the biome
    // sub-types (mirrors JobSystem.FogProfile / GroundProfile), so a "fog" biome
    // is exactly corridor fog and a "ground" biome exactly ground smoke.
    private static float FogProfile(Vector3 p, float coverage)
    {
        float midHeight = MathF.Exp(-MathF.Abs(p.Y - 1.0f) * 1.35f);
        float thickCoverage = 0.72f + 0.28f * coverage;
        float heightWeight = 0.70f + 0.30f * midHeight;
        // §O height ceiling (mirrors the shader FogProfile): the 0.70 floor otherwise fills all Y and
        // billows into the open sky. Fade it out from the wall top (2.0 = WALL_HEIGHT) upward so outdoor
        // fog is a low bank; below the wall top the factor is exactly 1.0, so roofed scenes stay bit-exact.
        float ct = Math.Clamp((p.Y - 2.0f) / 2.0f, 0f, 1f);
        float heightCeil = 1.0f - ct * ct * (3.0f - 2.0f * ct);
        return 0.50f * thickCoverage * heightWeight * heightCeil;
    }

    private static float GroundProfile(Vector3 p, float coverage)
    {
        float thickCoverage = 0.72f + 0.28f * coverage;
        float groundLayer = MathF.Exp(-MathF.Max(p.Y, 0f) * 2.35f);
        return 0.50f * thickCoverage * groundLayer;
    }

    private static float GetBiomeCellDensity(int biomeX, int biomeY, Vector3 p, float coverage)
    {
        if (!IsSmokeBiome(biomeX, biomeY))
            return 0f;

        return IsFogBiome(biomeX, biomeY)
            ? FogProfile(p, coverage)
            : GroundProfile(p, coverage);
    }

    private static float SmoothStep01(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // Narrow-band biome boundary blend (mirrors JobSystem.BiomeEdgeBlend): pure
    // biome outside [0.5 - halfBand, 0.5 + halfBand].
    private static float BiomeEdgeBlend(float f)
    {
        const float halfBand = 0.2f;
        float t = Math.Clamp((f - (0.5f - halfBand)) / (2f * halfBand), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // ── Smoke turbulence (texture-free 3D value-noise fBm) ─────────────
    // Mirrors the private JobSystem.SmokeTurbulence in PathTracer.cs: a wispy,
    // clumpy 3D density multiplier so the medium billows instead of reading as a
    // flat depth haze. Pure integer-hash math, identical to the HLSL port.

    private static float Hash3ToUnit(int x, int y, int z)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + z * 1013904223);
        h ^= h >> 16;
        h *= 2246822519u;
        h ^= h >> 13;
        h *= 3266489917u;
        h ^= h >> 16;
        return h * (1f / 4294967296f);
    }

    private static float ValueNoise3D(Vector3 p)
    {
        float fx = MathF.Floor(p.X), fy = MathF.Floor(p.Y), fz = MathF.Floor(p.Z);
        int ix = (int)fx, iy = (int)fy, iz = (int)fz;
        float tx = p.X - fx, ty = p.Y - fy, tz = p.Z - fz;
        float ux = tx * tx * (3f - 2f * tx);
        float uy = ty * ty * (3f - 2f * ty);
        float uz = tz * tz * (3f - 2f * tz);

        float c000 = Hash3ToUnit(ix, iy, iz);
        float c100 = Hash3ToUnit(ix + 1, iy, iz);
        float c010 = Hash3ToUnit(ix, iy + 1, iz);
        float c110 = Hash3ToUnit(ix + 1, iy + 1, iz);
        float c001 = Hash3ToUnit(ix, iy, iz + 1);
        float c101 = Hash3ToUnit(ix + 1, iy, iz + 1);
        float c011 = Hash3ToUnit(ix, iy + 1, iz + 1);
        float c111 = Hash3ToUnit(ix + 1, iy + 1, iz + 1);

        float x00 = c000 + (c100 - c000) * ux;
        float x10 = c010 + (c110 - c010) * ux;
        float x01 = c001 + (c101 - c001) * ux;
        float x11 = c011 + (c111 - c011) * ux;
        float y0 = x00 + (x10 - x00) * uy;
        float y1 = x01 + (x11 - x01) * uy;
        return y0 + (y1 - y0) * uz;
    }

    private static float SmokeFbm(Vector3 p)
    {
        float sum = 0f;
        float amp = 0.5f;
        for (int o = 0; o < 3; o++)
        {
            sum += amp * ValueNoise3D(p);
            p *= 2.02f;
            amp *= 0.5f;
        }
        return sum;
    }

    /// <summary>Density multiplier shaping wispy clumps and gaps (mirrors the
    /// HLSL <c>SmokeTurbulence</c>). <paramref name="time"/> drifts the noise
    /// domain so the fog animates; <c>time = 0</c> reproduces the static field the
    /// CPU renderer uses (so parity tests pass unchanged).</summary>
    public static float SmokeTurbulence(Vector3 p, float time = 0f)
    {
        var q = new Vector3(p.X * 0.22f + 11.3f, p.Y * 0.40f + 4.7f, p.Z * 0.22f + 19.1f);
        q += time * new Vector3(0.30f, 0.10f, 0.18f);
        float f = SmokeFbm(q);
        float n = Math.Clamp((f - 0.33f) * 2.6f, 0f, 1f);
        n = n * n * (3f - 2f * n);
        return 0.05f + 2.15f * n;
    }

    /// <summary>Biome-banded density: bilinear blend of the four surrounding
    /// biome cells' density, sculpted by the 3D turbulence (mirrors
    /// <c>JobSystem.GetDensityBiome</c>).</summary>
    public static float GetDensityBiome(Vector3 p, float time = 0f)
    {
        float biomeWorldSize = MazeGeometryBuilder.CellSize * BiomeSizeCells;

        // Centred on biome centres + narrow-band edge blend (mirrors the HLSL /
        // JobSystem): a biome's interior is pure, no half-biome bleed.
        float gx = p.X / biomeWorldSize - 0.5f;
        float gy = p.Z / biomeWorldSize - 0.5f;

        int bx0 = (int)MathF.Floor(gx);
        int by0 = (int)MathF.Floor(gy);
        int bx1 = bx0 + 1;
        int by1 = by0 + 1;

        float tx = BiomeEdgeBlend(gx - bx0);
        float ty = BiomeEdgeBlend(gy - by0);

        float coverage = SmokeCoverage(p.X, p.Z);
        float d00 = GetBiomeCellDensity(bx0, by0, p, coverage);
        float d10 = GetBiomeCellDensity(bx1, by0, p, coverage);
        float d01 = GetBiomeCellDensity(bx0, by1, p, coverage);
        float d11 = GetBiomeCellDensity(bx1, by1, p, coverage);

        float dx0 = d00 + (d10 - d00) * tx;
        float dx1 = d01 + (d11 - d01) * tx;
        return (dx0 + (dx1 - dx0) * ty) * SmokeTurbulence(p, time);
    }

    /// <summary>Corridor fog density (mirrors <c>JobSystem.GetDensityFog</c>).</summary>
    public static float GetDensityFog(Vector3 p, float time = 0f)
    {
        float coverage = SmokeCoverage(p.X, p.Z);
        return FogProfile(p, coverage) * SmokeTurbulence(p, time);
    }

    /// <summary>Ground smoke density (mirrors <c>JobSystem.GetDensityGround</c>).</summary>
    public static float GetDensityGround(Vector3 p, float time = 0f)
    {
        float coverage = SmokeCoverage(p.X, p.Z);
        return GroundProfile(p, coverage) * SmokeTurbulence(p, time);
    }

    /// <summary>Density at <paramref name="p"/> for the given smoke mode
    /// (mirrors <c>JobSystem.GetDensity</c>).</summary>
    public static float GetDensity(Vector3 p, SmokeMode smokeMode, float time = 0f)
        => smokeMode switch
        {
            SmokeMode.None => 0f,
            SmokeMode.Biome => GetDensityBiome(p, time),
            SmokeMode.AlwaysFog => GetDensityFog(p, time),
            SmokeMode.AlwaysGroundSmoke => GetDensityGround(p, time),
            _ => GetDensityBiome(p, time)
        };

    // ── In-scatter + phase (mirrors JobSystem in VolumetricIntegration.cs) ─

    private static float GetSigmaScale(VolumetricOptions options, SmokeMode smokeMode)
        => smokeMode == SmokeMode.AlwaysGroundSmoke ? options.SigmaScaleGround : options.SigmaScaleFog;

    /// <summary>Henyey–Greenstein phase toward <c>lights[0]</c>; isotropic when
    /// anisotropy is ~0 or there are no lights (mirrors <c>EvaluatePhase</c>).</summary>
    private static float EvaluatePhase(
        Vector3 viewDirection, Vector3 samplePoint, VolumetricOptions options,
        ReadOnlySpan<Vector3> lightPositions)
    {
        float g = Math.Clamp(options.AnisotropyG, -0.95f, 0.95f);
        if (MathF.Abs(g) <= 1e-4f || lightPositions.Length == 0)
            return IsotropicPhase;

        Vector3 lightDirection = Vector3.Normalize(lightPositions[0] - samplePoint);
        float cosTheta = Math.Clamp(Vector3.Dot(lightDirection, -viewDirection), -1f, 1f);
        float denom = 1f + g * g - 2f * g * cosTheta;
        return (1f - g * g) / (4f * MathF.PI * MathF.Pow(denom, 1.5f));
    }

    /// <summary>
    /// Local single-scatter radiance reaching <paramref name="samplePoint"/> from
    /// all lights, tinted by the medium (mirrors <c>EstimateInscatterLight</c>).
    /// When <paramref name="occluder"/> is non-null a shadow ray is cast per light
    /// (the HLSL passes a <c>traceShadow</c> bool + the TLAS instead).
    /// </summary>
    private static Vector3 EstimateInscatterLight(
        Vector3 samplePoint, Vector3 viewDirection, VolumetricOptions options,
        ReadOnlySpan<Vector3> lightPositions, ReadOnlySpan<Vector3> lightColors,
        Phase2Reference.ISceneTracer? occluder, float time)
    {
        if (lightPositions.Length == 0)
            return SmokeTint;

        Vector3 lighting = Vector3.Zero;
        for (int i = 0; i < lightPositions.Length; i++)
        {
            Vector3 toLight = lightPositions[i] - samplePoint;
            float distSq = MathF.Max(toLight.LengthSquared(), 1e-6f);
            float dist = MathF.Sqrt(distSq);
            Vector3 lightDir = toLight / dist;

            if (occluder is not null)
            {
                Vector3 shadowOrigin = samplePoint + lightDir * 1e-3f;
                if (occluder.Occluded(shadowOrigin, lightDir, dist - 2e-3f))
                    continue;
            }

            float lightWeight = LightIntensity / distSq;
            // Fog self-shadow (shadows-and-caustics-plan §A2): mirrors the CPU
            // JobSystem.EstimateInscatterLight so the GPU dims in-scatter through thick smoke.
            if (options.ShadowTransmittance && occluder is not null)
                lightWeight *= InscatterShadowTransmittance(samplePoint, lightDir, dist, options, time);

            lighting += lightColors[i] * lightWeight;
        }

        if (lighting == Vector3.Zero)
            return SmokeTint * 0.08f;

        Vector3 ambient = SmokeTint * AmbientLevel;
        return Vector3.Clamp(ambient + lighting * SmokeTint, Vector3.Zero, new Vector3(1.5f));
    }

    /// <summary>
    /// Fog transmittance in [0,1] from an in-scatter sample point toward a light, marched over the
    /// density field so the medium shadows itself. A line-for-line mirror of the CPU
    /// <c>JobSystem.InscatterShadowTransmittance</c> (shadows-and-caustics-plan §A2), advecting the
    /// density with <paramref name="time"/> exactly as the camera march does.
    /// </summary>
    private static float InscatterShadowTransmittance(
        Vector3 from, Vector3 lightDir, float dist, VolumetricOptions options, float time)
    {
        float marchLength = MathF.Min(dist, MathF.Max(options.MaxMarchDistance, 0f));
        if (marchLength <= 1e-5f)
            return 1f;

        int steps = Math.Max(2, options.MarchSteps / 4);
        float stepLength = marchLength / steps;
        float sigmaScale = GetSigmaScale(options, options.SmokeMode);
        float transmittance = 1f;

        for (int i = 0; i < steps; i++)
        {
            float distance = (i + 0.5f) * stepLength;
            Vector3 p = from + lightDir * distance;
            float density = GetDensity(p, options.SmokeMode, time);
            if (density <= 0f)
                continue;

            float sigmaT = density * sigmaScale;
            if (sigmaT <= 0f)
                continue;

            transmittance *= MathF.Exp(-sigmaT * stepLength);
            if (transmittance < options.EarlyOutTransmittance)
                return transmittance;
        }

        return transmittance;
    }

    /// <summary>
    /// Ray-marches the camera segment [<paramref name="rayOrigin"/> →
    /// <paramref name="hitPoint"/>] through the participating medium and returns
    /// the segment transmittance and accumulated in-scatter. A line-for-line
    /// mirror of <c>JobSystem.IntegrateVolumetricSegment</c> (the private overload
    /// the renderer actually calls, with lights + shadow BVH).
    /// </summary>
    /// <param name="occluder">Shadow-ray caster used on the steps selected by
    /// <see cref="VolumetricOptions.ShadowStepInterval"/>; pass null to disable
    /// shadow tracing entirely (the ambient-only inscatter used by Low/Medium).</param>
    public static VolumetricSample IntegrateSegment(
        Vector3 rayOrigin, Vector3 hitPoint, Vector3 rayDirection,
        VolumetricOptions options, bool isMoving,
        ReadOnlySpan<Vector3> lightPositions, ReadOnlySpan<Vector3> lightColors,
        Phase2Reference.ISceneTracer? occluder, float time = 0f)
    {
        if (!options.EnableVolumetrics || options.SmokeMode == SmokeMode.None || options.MarchSteps <= 0)
            return new VolumetricSample(1f, Vector3.Zero);

        Vector3 segment = hitPoint - rayOrigin;
        float rayLength = segment.Length();
        if (rayLength <= 1e-5f)
            return new VolumetricSample(1f, Vector3.Zero);

        float marchLength = MathF.Min(rayLength, MathF.Max(options.MaxMarchDistance, 0f));
        if (marchLength <= 1e-5f)
            return new VolumetricSample(1f, Vector3.Zero);

        // Full step count regardless of motion (mirrors the shader) — halving while moving made the fog
        // shift when the camera started/stopped.
        int steps = Math.Max(1, options.MarchSteps);

        Vector3 dir = segment / rayLength;
        if (rayDirection.LengthSquared() > 1e-10f)
            dir = Vector3.Normalize(rayDirection);

        float stepLength = marchLength / steps;
        float transmittance = 1f;
        Vector3 inscatter = Vector3.Zero;

        float sigmaScale = GetSigmaScale(options, options.SmokeMode);
        float inscatterScale = options.InscatterStrength / IsotropicPhase;

        for (int i = 0; i < steps; i++)
        {
            float distance = (i + 0.5f) * stepLength;
            Vector3 p = rayOrigin + dir * distance;
            float density = GetDensity(p, options.SmokeMode, time);
            if (density <= 0f)
                continue;

            float sigmaT = density * sigmaScale;
            if (sigmaT <= 0f)
                continue;

            float opticalDepth = sigmaT * stepLength;
            float stepTransmittance = MathF.Exp(-opticalDepth);
            float scatterWeight = transmittance * (1f - stepTransmittance);
            float phase = EvaluatePhase(dir, p, options, lightPositions);

            bool traceShadow = options.ShadowStepInterval > 0
                && i % options.ShadowStepInterval == 0;
            Vector3 localLight = EstimateInscatterLight(
                p, dir, options, lightPositions, lightColors, traceShadow ? occluder : null, time);

            inscatter += localLight * (scatterWeight * inscatterScale * phase);
            transmittance *= stepTransmittance;

            if (transmittance < options.EarlyOutTransmittance)
                break;
        }

        return new VolumetricSample(transmittance, inscatter);
    }

    /// <summary>
    /// Composites a volumetric sample onto an already deterministic-corrected
    /// surface XYZ. Equivalent to the CPU's raw-space <c>xyz * T + Inscatter</c>
    /// followed by the scalar correction, since
    /// <c>(xyz·T + Inscatter)·k == (xyz·k)·T + Inscatter·k</c>.
    /// </summary>
    public static Vector3 Apply(Vector3 correctedSurfaceXyz, VolumetricSample sample, float correction)
        => correctedSurfaceXyz * sample.Transmittance + sample.Inscatter * correction;
}
