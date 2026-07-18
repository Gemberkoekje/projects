using System.Numerics;

namespace RayTracer;

/// <summary>
/// Result of camera-segment participating media integration.
/// </summary>
public readonly record struct VolumetricSample(float Transmittance, Vector3 Inscatter)
{
    public Vector3 Apply(Vector3 surfaceXyz) => surfaceXyz * Transmittance + Inscatter;
}

public partial class JobSystem
{
    private const float IsotropicPhase = 0.07957747f;
    private static readonly Vector3 SmokeTint = new(0.97f, 0.97f, 0.97f);

    public static VolumetricSample IntegrateVolumetricSegment(
        Vector3 rayOrigin,
        Vector3 hitPoint,
        Vector3 rayDirection,
        VolumetricOptions options)
    {
        return IntegrateVolumetricSegment(rayOrigin, hitPoint, rayDirection, options, false, [], default);
    }

    private static VolumetricSample IntegrateVolumetricSegment(
        Vector3 rayOrigin,
        Vector3 hitPoint,
        Vector3 rayDirection,
        VolumetricOptions options,
        bool isMoving,
        Light[] lights,
        BVH? bvh)
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

        // Full step count regardless of motion — halving it while moving made the fog integral jump when
        // the camera started/stopped (a flicker). Mirrors the shader IntegrateVolumetric.
        int steps = Math.Max(1, options.MarchSteps);

        Vector3 dir = segment / rayLength;
        if (rayDirection.LengthSquared() > 1e-10f)
            dir = Vector3.Normalize(rayDirection);

        float stepLength = marchLength / steps;
        float transmittance = 1f;
        Vector3 inscatter = Vector3.Zero;

        // Hoist loop-invariant values out of the per-step iteration.
        float sigmaScale = GetSigmaScale(options, options.SmokeMode);
        float inscatterScale = options.InscatterStrength / IsotropicPhase;

        // Finding §2: when shadow tracing on a cadence (ShadowStepInterval > 0), the steps between
        // traces reuse the last *traced* in-scatter visibility instead of adding the light fully
        // unoccluded. The old "unoccluded on 3 of every 4 steps" washed out fog shadows / god-rays.
        Vector3 cachedInscatterLight = Vector3.Zero;
        bool haveCachedInscatter = false;

        for (int i = 0; i < steps; i++)
        {
            float distance = (i + 0.5f) * stepLength;
            Vector3 p = rayOrigin + dir * distance;
            float density = GetDensity(p, options.SmokeMode);
            if (density <= 0f)
                continue;

            float sigmaT = density * sigmaScale;
            if (sigmaT <= 0f)
                continue;

            float opticalDepth = sigmaT * stepLength;
            float stepTransmittance = MathF.Exp(-opticalDepth);
            float scatterWeight = transmittance * (1f - stepTransmittance);
            // §9: PhaseAllLights folds the per-light phase into EstimateInscatterLight, so the outer factor
            // is the neutral isotropic phase; otherwise the single lights[0] phase multiplies the whole sum.
            float phase = options.PhaseAllLights ? IsotropicPhase : EvaluatePhase(dir, p, options, lights);

            // Query the BVH shadow ray on steps that fall on the configured interval (0 = never,
            // 1 = every step, N = every Nth step); reuse the last traced visibility in between.
            Vector3 localLight;
            if (options.ShadowStepInterval <= 0)
            {
                // No fog shadowing requested (cheap tiers) — unoccluded in-scatter every step, unchanged.
                localLight = EstimateInscatterLight(p, dir, options, lights, null);
            }
            else
            {
                bool traceShadow = i % options.ShadowStepInterval == 0;
                if (traceShadow || !haveCachedInscatter)
                {
                    cachedInscatterLight = EstimateInscatterLight(p, dir, options, lights, bvh);
                    haveCachedInscatter = true;
                }
                localLight = cachedInscatterLight;
            }

            // §9: single-scattering albedo scales how much of the extinction scatters toward the eye
            // (the rest is absorbed); 1 (default) is the historical non-absorbing smoke, <1 lets thick
            // smoke darken what is behind it. Transmittance still uses the full extinction.
            inscatter += localLight * (scatterWeight * inscatterScale * phase * options.SingleScatterAlbedo);
            transmittance *= stepTransmittance;

            if (transmittance < options.EarlyOutTransmittance)
                break;
        }

        return new VolumetricSample(transmittance, inscatter);
    }

    internal VolumetricSample IntegrateVolumetricSegment(Vector3 rayOrigin, Vector3 hitPoint, Vector3 rayDirection)
    {
        // §9: _fogLights carries the blackbody-tinted colours when FogSpectralLighting is on (else it
        // aliases _lights). Positions/directions are unchanged, so shadow rays and the phase are unaffected.
        return IntegrateVolumetricSegment(rayOrigin, hitPoint, rayDirection, Volumetrics, IsMoving, _fogLights, _bvh);
    }

    /// <summary>
    /// Transmittance in [0,1] of the participating medium along the segment
    /// [<paramref name="from"/> → <paramref name="to"/>], with no in-scatter — the
    /// extinction-only companion to <see cref="IntegrateVolumetricSegment(Vector3,Vector3,Vector3,VolumetricOptions)"/>.
    /// Used by NEE shadow rays so moving fog dims the light reaching a surface and casts
    /// shadows (shadows-and-caustics-plan §A2). Returns exactly 1 (no attenuation) when
    /// volumetrics or the <see cref="VolumetricOptions.ShadowTransmittance"/> opt-in is off, so
    /// the cheaper presets are byte-for-byte unaffected. It marches at a coarse step count (half
    /// the camera march) since a shadow term tolerates more noise than the primary view.
    /// </summary>
    public static float SegmentTransmittance(
        Vector3 from,
        Vector3 to,
        Vector3 direction,
        VolumetricOptions options,
        bool isMoving)
    {
        if (!options.EnableVolumetrics || options.SmokeMode == SmokeMode.None
            || !options.ShadowTransmittance || options.MarchSteps <= 0)
            return 1f;

        Vector3 segment = to - from;
        float rayLength = segment.Length();
        if (rayLength <= 1e-5f)
            return 1f;

        float marchLength = MathF.Min(rayLength, MathF.Max(options.MaxMarchDistance, 0f));
        if (marchLength <= 1e-5f)
            return 1f;

        int steps = Math.Max(2, options.MarchSteps / 2);
        if (isMoving && steps > 1)
            steps = Math.Max(1, (steps + 1) / 2);

        Vector3 dir = segment / rayLength;
        if (direction.LengthSquared() > 1e-10f)
            dir = Vector3.Normalize(direction);

        float stepLength = marchLength / steps;
        float transmittance = 1f;
        float sigmaScale = GetSigmaScale(options, options.SmokeMode);

        for (int i = 0; i < steps; i++)
        {
            float distance = (i + 0.5f) * stepLength;
            Vector3 p = from + dir * distance;
            float density = GetDensity(p, options.SmokeMode);
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

    internal float SegmentTransmittance(Vector3 from, Vector3 to, Vector3 direction)
        => SegmentTransmittance(from, to, direction, Volumetrics, IsMoving);

    private static float GetSigmaScale(VolumetricOptions options, SmokeMode smokeMode)
    {
        return smokeMode == SmokeMode.AlwaysGroundSmoke ? options.SigmaScaleGround : options.SigmaScaleFog;
    }

    /// <summary>
    /// Fog transmittance in [0,1] from an in-scatter sample point toward a light, marched over the
    /// density field so the medium shadows itself (shadows-and-caustics-plan §A2). A coarse march
    /// (a quarter of the camera step count) — a self-shadow term tolerates more noise than the
    /// primary view. Mirrors <c>Phase4Reference.InscatterShadowTransmittance</c> for GPU parity.
    /// </summary>
    private static float InscatterShadowTransmittance(Vector3 from, Vector3 lightDir, float dist, VolumetricOptions options)
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
            float density = GetDensity(p, options.SmokeMode);
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

    private static float EvaluatePhase(Vector3 viewDirection, Vector3 samplePoint, VolumetricOptions options, Light[] lights)
    {
        float g = Math.Clamp(options.AnisotropyG, -0.95f, 0.95f);
        if (MathF.Abs(g) <= 1e-4f || lights.Length == 0)
            return IsotropicPhase;

        Vector3 lightDirection = Vector3.Normalize(lights[0].Position - samplePoint);
        float cosTheta = Math.Clamp(Vector3.Dot(lightDirection, -viewDirection), -1f, 1f);
        float denom = 1f + g * g - 2f * g * cosTheta;
        return (1f - g * g) / (4f * MathF.PI * MathF.Pow(denom, 1.5f));
    }

    private static Vector3 EstimateInscatterLight(Vector3 samplePoint, Vector3 viewDirection, VolumetricOptions options, Light[] lights, BVH? bvh)
    {
        if (lights.Length == 0)
            return SmokeTint;

        Vector3 lighting = Vector3.Zero;
        for (int i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            Vector3 lightDir; float dist; float lightWeight;
            if (light.Directional) // §C2/§O8: directional sun — parallel, no falloff, dark below the horizon
            {
                Vector3 toSun = Vector3.Normalize(-light.Direction);
                if (toSun.Y <= 0f) continue; // night: a below-horizon sun lights no fog
                lightDir = toSun;
                dist = 1e4f;
                lightWeight = LightIntensity; // parallel rays: full strength, no inverse-square
            }
            else
            {
                Vector3 toLight = light.Position - samplePoint;
                float distSq = MathF.Max(toLight.LengthSquared(), 1e-6f);
                dist = MathF.Sqrt(distSq);
                lightDir = toLight / dist;
                lightWeight = LightIntensity / distSq;
            }

            if (bvh is not null)
            {
                var shadowRay = new Ray
                {
                    Origin = samplePoint + lightDir * 1e-3f,
                    Direction = lightDir,
                    Wavelength = 555,
                    Intensity = 1f,
                };

                if (bvh.IsOccluded(shadowRay, dist - 2e-3f))
                    continue;
            }

            // Fog self-shadow (shadows-and-caustics-plan §A2): thick fog between this sample and
            // the light dims the in-scatter, so shafts fall off with depth into the smoke. Tied to
            // the same shadow-step cadence (bvh non-null) as the geometry occlusion above.
            if (options.ShadowTransmittance && bvh is not null)
                lightWeight *= InscatterShadowTransmittance(samplePoint, lightDir, dist, options);

            // §9: with PhaseAllLights, weight each light's in-scatter by the Henyey–Greenstein phase toward
            // *that* light (normalised by the isotropic phase, the neutral outer factor), instead of the
            // single lights[0] phase applied to the whole sum. No anisotropy (g≈0) → 1, so it is a no-op.
            float phaseFactor = 1f;
            if (options.PhaseAllLights)
            {
                float g = Math.Clamp(options.AnisotropyG, -0.95f, 0.95f);
                if (MathF.Abs(g) > 1e-4f)
                {
                    float cosTheta = Math.Clamp(Vector3.Dot(lightDir, -viewDirection), -1f, 1f);
                    float denom = 1f + g * g - 2f * g * cosTheta;
                    phaseFactor = (1f - g * g) / (4f * MathF.PI * MathF.Pow(denom, 1.5f)) / IsotropicPhase;
                }
            }

            lighting += light.Color * (lightWeight * phaseFactor);
        }

        if (lighting == Vector3.Zero)
            return SmokeTint * 0.08f;

        Vector3 ambient = SmokeTint * AmbientLevel;
        return Vector3.Clamp(ambient + lighting * SmokeTint, Vector3.Zero, new Vector3(1.5f));
    }
}
