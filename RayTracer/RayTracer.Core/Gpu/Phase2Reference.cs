using System;
using System.Collections.Generic;
using System.Numerics;

namespace RayTracer;

/// <summary>
/// A CPU replica of the Phase 2 (lighting) GPU shader's pure math: next-event
/// estimation direct lighting with stochastic light selection and shadow rays,
/// plus a one-bounce + tertiary-bounce indirect estimator. <c>PathTracePhase2.hlsl</c>
/// is a line-for-line port of the methods here, and the unit tests pin this
/// replica to the CPU renderer's own <c>JobSystem.TraceCore</c> output (via the
/// real <see cref="BVH"/> and <see cref="Light"/> array). That makes shader/CPU
/// lighting parity testable in plain C#, where the GPU cannot run.
///
/// This mirrors the <see cref="LightingMode"/> branches of
/// <c>JobSystem.TraceCore</c> with the diffuse irradiance cache disabled — the
/// GPU port drops the cache (see the plan's Phase 2.4), so every sample traces
/// the tertiary ray, exactly as the cache-off CPU path does.
/// </summary>
public static class Phase2Reference
{
    /// <summary>Base ambient illumination applied to every hit
    /// (mirrors <c>JobSystem.AmbientLevel</c>).</summary>
    public const float AmbientLevel = 0.01f;

    /// <summary>Intensity of each point light
    /// (mirrors <c>JobSystem.LightIntensity</c>).</summary>
    public const float LightIntensity = 1.5f;

    private const uint RngMul = 747796405u;
    private const uint RngAdd = 2891336453u;

    /// <summary>
    /// Ray-cast interface the shading uses, so the same math can run over the
    /// CPU <see cref="BVH"/> (in tests) and — mirrored in HLSL — over the
    /// hardware acceleration structure. Both entry points return quad indices in
    /// packer order (the shader derives them from <c>PrimitiveIndex &gt;&gt; 1</c>).
    /// </summary>
    public interface ISceneTracer
    {
        /// <summary>Closest hit of the ray; <paramref name="quadIndex"/> indexes the packed primitives.</summary>
        bool ClosestHit(Vector3 origin, Vector3 dir, out int quadIndex, out Vector3 hitPoint);

        /// <summary>True if any geometry occludes the ray before <paramref name="maxDist"/>.</summary>
        bool Occluded(Vector3 origin, Vector3 dir, float maxDist);
    }

    /// <summary>The oriented (face) normal at a hit: the stored normal flipped to
    /// oppose the ray, matching <c>denom &lt; 0 ? Normal : -Normal</c> in the
    /// rectangle <c>Intersect</c> methods.</summary>
    public static Vector3 FaceNormal(in GpuPrimitive prim, Vector3 rayDir)
    {
        Vector3 n = new(prim.NX, prim.NY, prim.NZ);
        return Vector3.Dot(rayDir, n) < 0f ? n : -n;
    }

    /// <summary>Single-hero-wavelength diffuse albedo XYZ at a hit (no correction),
    /// used for the indirect bounces where only the hero wavelength is carried.</summary>
    public static Vector3 IndirectBaseXyz(
        in GpuPrimitive prim, SpectralResources res, Vector3 rayDir, Vector3 hitPoint, uint heroIdx)
    {
        Phase1Reference.PatternShade(prim, rayDir, hitPoint, out uint row, out float atten);
        float refl = res.MaterialReflectance[(int)row * res.DeterministicCount + (int)heroIdx] * atten;
        refl *= Phase1Reference.ThinFilmFactor(prim, rayDir, hitPoint, res.DeterWavelengths[heroIdx]);
        var cie = new Vector3(
            res.DeterXYZ[heroIdx * 4 + 0],
            res.DeterXYZ[heroIdx * 4 + 1],
            res.DeterXYZ[heroIdx * 4 + 2]);
        return cie * refl;
    }

    /// <summary>
    /// Cosine-weighted hemisphere sample about <paramref name="n"/> using the
    /// same tangent-frame construction as <c>TraceCore</c>.
    /// </summary>
    public static Vector3 CosineHemisphere(Vector3 n, float r1, float r2)
    {
        float sqrtR1 = MathF.Sqrt(r1);
        float theta = 2f * MathF.PI * r2;
        float sx = sqrtR1 * MathF.Cos(theta);
        float sy = sqrtR1 * MathF.Sin(theta);
        float sz = MathF.Sqrt(MathF.Max(0f, 1f - r1));

        Vector3 tangent = MathF.Abs(n.X) > 0.1f
            ? Vector3.Normalize(new Vector3(n.Y, -n.X, 0f))
            : Vector3.Normalize(new Vector3(0f, n.Z, -n.Y));
        Vector3 bitangent = Vector3.Cross(n, tangent);
        return Vector3.Normalize(sx * tangent + sy * bitangent + sz * n);
    }

    /// <summary>
    /// Importance-samples one light for NEE, mirroring <c>TraceCore</c>'s local
    /// <c>SelectLight</c>. Weights are inverse-square, cosine-gated; the pick
    /// walks the CDF against a single RNG draw. The weights are recomputed rather
    /// than cached in an array so the HLSL port needs no per-thread
    /// <c>MAX_LIGHTS</c> buffer; the recomputation is bit-identical, so the pick
    /// and returned pdf match the CPU exactly.
    /// </summary>
    /// <returns>The selected light index.</returns>
    public static int SelectLight(
        ref uint rng, ReadOnlySpan<Vector3> lights, Vector3 samplePoint, Vector3 normal,
        out float outP, out Vector3 outDir, out float outDistSq, out float outCos)
    {
        int nLights = lights.Length;

        float totalW = 0f;
        for (int li = 0; li < nLights; li++)
        {
            totalW += LightWeight(lights[li], samplePoint, normal);
        }

        rng = rng * RngMul + RngAdd;

        if (totalW > 0f)
        {
            float u = rng / 4294967296f * totalW;
            float acc = 0f;
            for (int li = 0; li < nLights; li++)
            {
                float w = LightWeight(lights[li], samplePoint, normal);
                acc += w;
                if (u <= acc)
                {
                    FillLight(lights[li], samplePoint, normal, out outDir, out outDistSq, out outCos);
                    outP = w / totalW;
                    return li;
                }
            }

            int last = nLights - 1;
            FillLight(lights[last], samplePoint, normal, out outDir, out outDistSq, out outCos);
            outP = LightWeight(lights[last], samplePoint, normal) / totalW;
            return last;
        }

        int idx = (int)(rng % (uint)nLights);
        FillLight(lights[idx], samplePoint, normal, out outDir, out outDistSq, out outCos);
        outP = 1f / nLights;
        return idx;
    }

    private static float LightWeight(Vector3 lightPos, Vector3 samplePoint, Vector3 normal)
    {
        Vector3 toLight = lightPos - samplePoint;
        float dsq = Vector3.Dot(toLight, toLight);
        float dist = MathF.Sqrt(dsq);
        Vector3 ldir = toLight / dist;
        float cos = Vector3.Dot(normal, ldir);
        return cos > 0f ? 1f / MathF.Max(dsq, 1e-6f) : 0f;
    }

    private static void FillLight(
        Vector3 lightPos, Vector3 samplePoint, Vector3 normal,
        out Vector3 outDir, out float outDistSq, out float outCos)
    {
        Vector3 toLight = lightPos - samplePoint;
        outDistSq = MathF.Max(Vector3.Dot(toLight, toLight), 1e-6f);
        float dist = MathF.Sqrt(outDistSq);
        outDir = toLight / dist;
        outCos = Vector3.Dot(normal, outDir);
    }

    /// <summary>
    /// Computes the full deterministic-corrected XYZ contribution of one sample
    /// at pixel (hash <paramref name="pixelHash"/>, index <paramref name="sampleIdx"/>),
    /// exactly as <c>TraceCore</c> does for the given <paramref name="lighting"/>
    /// mode (with the diffuse cache disabled). Also returns the direct and
    /// indirect corrected components (the GPU's per-bounce debug channels).
    /// Returns <see cref="Vector3.Zero"/> for a background (missed) primary ray.
    /// </summary>
    public static Vector3 ShadeSample(
        ISceneTracer tracer,
        IReadOnlyList<GpuPrimitive> prims,
        SpectralResources res,
        ReadOnlySpan<Vector3> lights,
        LightingMode lighting,
        Vector3 camPos,
        Vector3 primaryDir,
        uint pixelHash,
        uint sampleIdx,
        out Vector3 correctedDirect,
        out Vector3 correctedIndirect)
    {
        correctedDirect = Vector3.Zero;
        correctedIndirect = Vector3.Zero;

        if (!tracer.ClosestHit(camPos, primaryDir, out int primIndex, out Vector3 hitPoint))
            return Vector3.Zero;

        GpuPrimitive prim = prims[primIndex];
        Vector3 hitNormal = FaceNormal(prim, primaryDir);

        int deterCount = res.DeterministicCount;
        uint heroIdx = Phase1Reference.HeroIndex(pixelHash, sampleIdx, deterCount);

        // Specular surface — mirror (§1.1) or dielectric reflect/refract (§1.2).
        // Hero-wavelength sampling; accumulation builds the spectrum. A line-for-line
        // replica of JobSystem.TraceCore's specular branch.
        if (Optics.IsSpecular((SurfaceKind)prim.Surface))
        {
            Phase1Reference.PatternShade(prim, primaryDir, hitPoint, out uint mRow, out float mAtten);
            float specularRefl = res.MaterialReflectance[(int)mRow * deterCount + (int)heroIdx] * mAtten;

            uint specularRng = pixelHash + sampleIdx * 2654435761u + 1013904223u;
            Vector3 specularXyz = SpecularRadiance(
                tracer, prims, res, lights, lighting, (SurfaceKind)prim.Surface, specularRefl, IorAtHero(prim, res, heroIdx),
                AbsorptionAtHero(prim, res, heroIdx), hitPoint, hitNormal, primaryDir, heroIdx, ref specularRng, 0, inGlass: false, mediumSigma: 0f);

            float specularCorrection = res.DeterministicCorrection;
            correctedDirect = specularXyz * specularCorrection;
            return specularXyz * specularCorrection;
        }

        Vector3 baseXyz = Phase1Reference.BaseXyz(prim, res, primaryDir, hitPoint, pixelHash, sampleIdx);

        bool lit = lighting != LightingMode.None && lights.Length > 0;

        Vector3 xyz;
        // TraceCore only commits the direct AOV (bounce0) inside the indirect-hit
        // block; on a secondary miss it stays zero even though directTerm is
        // already folded into the total. Mirror that exactly (start at zero).
        Vector3 bounce0 = Vector3.Zero;
        Vector3 indirect = Vector3.Zero;

        if (!lit)
        {
            xyz = baseXyz;
            bounce0 = baseXyz;
        }
        else
        {
            float ambientTerm = AmbientLevel;

            // ── Direct lighting (NEE) ──────────────────────────────────
            float directTerm = 0f;
            uint rngLight = pixelHash + sampleIdx * RngMul + RngAdd;
            _ = SelectLight(ref rngLight, lights, hitPoint, hitNormal,
                out float lightP, out Vector3 lightDir, out float lightDistSq, out float lightCos);

            if (lightCos > 0f)
            {
                bool visible = true;
                if (lighting == LightingMode.NEE)
                {
                    Vector3 shadowOrigin = hitPoint + hitNormal * 1e-3f;
                    visible = !tracer.Occluded(shadowOrigin, lightDir, MathF.Sqrt(lightDistSq) - 2e-3f);
                }

                if (visible)
                    directTerm += lightCos / lightDistSq * LightIntensity / MathF.Max(lightP, 1e-9f);
            }

            xyz = baseXyz * (ambientTerm + directTerm);

            // ── Indirect: one bounce + tertiary bounce ─────────────────
            uint rng = pixelHash + sampleIdx * RngMul + RngAdd;
            rng = rng * RngMul + RngAdd;
            float r1 = (rng & 0xFFFF) / 65536f;
            rng = rng * RngMul + RngAdd;
            float r2 = (rng & 0xFFFF) / 65536f;

            Vector3 sampleDir = CosineHemisphere(hitNormal, r1, r2);
            Vector3 secOrigin = hitPoint + hitNormal * 1e-3f;

            if (tracer.ClosestHit(secOrigin, sampleDir, out int secIndex, out Vector3 secHitPoint))
            {
                GpuPrimitive secPrim = prims[secIndex];
                Vector3 secHitNormal = FaceNormal(secPrim, sampleDir);
                Vector3 secBaseXyz = IndirectBaseXyz(secPrim, res, sampleDir, secHitPoint, heroIdx);

                float secDirectTerm = 0f;
                rng = rng * RngMul + RngAdd;
                int lightIdx2 = (int)(rng % (uint)lights.Length);
                secDirectTerm += UniformLightTerm(tracer, lights, lightIdx2, secHitPoint, secHitNormal);

                Vector3 secIncoming = secBaseXyz * (AmbientLevel + secDirectTerm);

                Vector3 secBounce2Plus = Vector3.Zero;
                rng = rng * RngMul + RngAdd;
                float r3 = (rng & 0xFFFF) / 65536f;
                rng = rng * RngMul + RngAdd;
                float r4 = (rng & 0xFFFF) / 65536f;

                Vector3 tertDir = CosineHemisphere(secHitNormal, r3, r4);
                Vector3 tertOrigin = secHitPoint + secHitNormal * 1e-3f;

                if (tracer.ClosestHit(tertOrigin, tertDir, out int tertIndex, out Vector3 tertHitPoint))
                {
                    GpuPrimitive tertPrim = prims[tertIndex];
                    Vector3 tertHitNormal = FaceNormal(tertPrim, tertDir);
                    Vector3 tertBaseXyz = IndirectBaseXyz(tertPrim, res, tertDir, tertHitPoint, heroIdx);

                    float tertDirectTerm = 0f;
                    rng = rng * RngMul + RngAdd;
                    int lightIdx3 = (int)(rng % (uint)lights.Length);
                    tertDirectTerm += UniformLightTerm(tracer, lights, lightIdx3, tertHitPoint, tertHitNormal);

                    Vector3 tertIncoming = tertBaseXyz * (AmbientLevel + tertDirectTerm);
                    secBounce2Plus = secBaseXyz * tertIncoming;
                }

                bounce0 = baseXyz * directTerm;
                Vector3 localBounce1 = baseXyz * secIncoming;
                Vector3 localBounce2Plus = baseXyz * secBounce2Plus;
                indirect = localBounce1 + localBounce2Plus;
                xyz += indirect;
            }
        }

        float correction = res.DeterministicCorrection;
        correctedDirect = bounce0 * correction;
        correctedIndirect = indirect * correction;
        return xyz * correction;
    }

    /// <summary>Number of specular bounces followed through mirror/dielectric
    /// surfaces (mirrors <c>JobSystem.MaxSpecularBounces</c>).</summary>
    public const int MaxSpecularBounces = 8;

    /// <summary>
    /// The primitive's index of refraction at the hero wavelength — the GPU replica of
    /// <c>MaterialData.IorAt</c> for dispersion (spectral-effects-plan §2.1). The base
    /// IOR (<c>Cauchy A</c>) rides in <see cref="GpuPrimitive.Ior"/> and the dispersion
    /// coefficient (<c>Cauchy B</c>) in <see cref="GpuPrimitive.CauchyB"/>; a non-dispersive
    /// material (<c>CauchyB == 0</c>) yields the constant <see cref="GpuPrimitive.Ior"/>.
    /// </summary>
    private static float IorAtHero(GpuPrimitive prim, SpectralResources res, uint heroIdx)
    {
        if (prim.CauchyB == 0f) return prim.Ior;
        float um = res.DeterWavelengths[heroIdx] * 1e-3f;
        return prim.Ior + prim.CauchyB / (um * um);
    }

    /// <summary>The dielectric's Beer–Lambert absorption σ at the hero wavelength (§2.3) —
    /// the GPU replica of <c>MaterialData.ExtinctionAt</c>. The absorption anchors σ(R/G/B)
    /// (plus the uniform base) ride in the free pattern slots P0/P1/P2 for a dielectric.</summary>
    private static float AbsorptionAtHero(GpuPrimitive prim, SpectralResources res, uint heroIdx)
        => Optics.AbsorptionAt(prim.P0, prim.P1, prim.P2, res.DeterWavelengths[heroIdx]);

    /// <summary>
    /// Scalar spectral radiance at the hero wavelength arriving back along a
    /// specular ray: each mirror/dielectric hit interacts and recurses (up to
    /// <see cref="MaxSpecularBounces"/>); the first diffuse hit is shaded diffusely;
    /// a miss returns 0. A line-for-line replica of <c>JobSystem.TraceSpecularRadiance</c>.
    /// </summary>
    public static Vector3 TraceSpecularRadiance(
        ISceneTracer tracer, IReadOnlyList<GpuPrimitive> prims, SpectralResources res,
        ReadOnlySpan<Vector3> lights, LightingMode lighting,
        Vector3 origin, Vector3 dir, uint heroIdx, ref uint rng, int depth, bool inGlass, float mediumSigma)
    {
        if (!tracer.ClosestHit(origin, dir, out int idx, out Vector3 hitPoint))
            return Vector3.Zero;

        GpuPrimitive prim = prims[idx];
        Vector3 hitNormal = FaceNormal(prim, dir);
        Phase1Reference.PatternShade(prim, dir, hitPoint, out uint row, out float atten);
        float refl = res.MaterialReflectance[(int)row * res.DeterministicCount + (int)heroIdx] * atten;
        refl *= Phase1Reference.ThinFilmFactor(prim, dir, hitPoint, res.DeterWavelengths[heroIdx]); // iridescent surface seen in a reflection

        var surface = (SurfaceKind)prim.Surface;
        Vector3 radiance;
        if (Optics.IsSpecular(surface) && depth < MaxSpecularBounces)
        {
            radiance = SpecularRadiance(tracer, prims, res, lights, lighting, surface, refl, IorAtHero(prim, res, heroIdx),
                AbsorptionAtHero(prim, res, heroIdx), hitPoint, hitNormal, dir, heroIdx, ref rng, depth, inGlass, mediumSigma);
        }
        else
        {
            // Terminal diffuse hit → CIE-weighted XYZ (the GPU port does not model smoke in
            // reflections in this replica; the HLSL applies per-segment fog).
            float scalar = MirrorDiffuseScalar(tracer, lights, lighting, refl, hitPoint, hitNormal, ref rng);
            var cie = new Vector3(res.DeterXYZ[heroIdx * 4 + 0], res.DeterXYZ[heroIdx * 4 + 1], res.DeterXYZ[heroIdx * 4 + 2]);
            radiance = cie * scalar;
        }

        // Beer–Lambert absorption for the segment just travelled inside a coloured medium (§2.3).
        if (mediumSigma > 0f)
            radiance *= MathF.Exp(-mediumSigma * Vector3.Distance(origin, hitPoint));
        return radiance;
    }

    /// <summary>
    /// One specular interaction (mirror reflect, or dielectric Fresnel reflect/refract
    /// via Russian roulette through <see cref="Optics"/>), then recurses. A line-for-line
    /// replica of <c>JobSystem.SpecularRadiance</c>.
    /// </summary>
    public static Vector3 SpecularRadiance(
        ISceneTracer tracer, IReadOnlyList<GpuPrimitive> prims, SpectralResources res,
        ReadOnlySpan<Vector3> lights, LightingMode lighting,
        SurfaceKind surface, float reflectance, float ior, float hitExtinction,
        Vector3 hitPoint, Vector3 hitNormal, Vector3 incidentDir, uint heroIdx, ref uint rng, int depth, bool inGlass, float mediumSigma)
    {
        if (surface == SurfaceKind.Mirror)
        {
            Vector3 reflectDir = Vector3.Reflect(incidentDir, hitNormal);
            Vector3 reflectOrigin = hitPoint + hitNormal * 1e-3f;
            return reflectance * TraceSpecularRadiance(
                tracer, prims, res, lights, lighting, reflectOrigin, reflectDir, heroIdx, ref rng, depth + 1, inGlass, mediumSigma);
        }

        // Dielectric glass: reflect vs. refract by Russian roulette on Fresnel.
        float iorFrom = inGlass ? ior : 1f;
        float iorTo = inGlass ? 1f : ior;
        float cosI = MathF.Abs(Vector3.Dot(Vector3.Normalize(incidentDir), hitNormal));
        float fresnel = Optics.FresnelDielectric(cosI, iorFrom, iorTo);
        bool transmits = Optics.Refract(incidentDir, hitNormal, iorFrom, iorTo, out Vector3 refractDir);

        rng = rng * RngMul + RngAdd;
        float u = rng / 4294967296f;
        if (!transmits || u < fresnel)
        {
            Vector3 reflectDir = Vector3.Reflect(incidentDir, hitNormal);
            Vector3 reflectOrigin = hitPoint + hitNormal * 1e-3f;
            return TraceSpecularRadiance(
                tracer, prims, res, lights, lighting, reflectOrigin, reflectDir, heroIdx, ref rng, depth + 1, inGlass, mediumSigma);
        }

        Vector3 refractOrigin = hitPoint - hitNormal * 1e-3f;
        bool newInGlass = !inGlass;
        float newMediumSigma = newInGlass ? hitExtinction : 0f; // enter this glass's absorption, or exit to clear air (§2.3)
        return TraceSpecularRadiance(
            tracer, prims, res, lights, lighting, refractOrigin, refractDir, heroIdx, ref rng, depth + 1, newInGlass, newMediumSigma);
    }

    /// <summary><c>reflectance × (ambient + direct)</c> at a diffuse surface reached
    /// through a mirror, matching <c>JobSystem.ShadeDiffuseScalar</c> /
    /// <c>DirectTermAt</c> (same importance-sampled NEE as the primary path).</summary>
    private static float MirrorDiffuseScalar(
        ISceneTracer tracer, ReadOnlySpan<Vector3> lights, LightingMode lighting,
        float reflectance, Vector3 point, Vector3 normal, ref uint rng)
    {
        float ambient = 1f;
        float direct = 0f;
        if (lighting != LightingMode.None && lights.Length > 0)
        {
            ambient = AmbientLevel;
            _ = SelectLight(ref rng, lights, point, normal,
                out float lightP, out Vector3 lightDir, out float lightDistSq, out float lightCos);
            if (lightCos > 0f)
            {
                bool visible = true;
                if (lighting == LightingMode.NEE)
                {
                    Vector3 shadowOrigin = point + normal * 1e-3f;
                    visible = !tracer.Occluded(shadowOrigin, lightDir, MathF.Sqrt(lightDistSq) - 2e-3f);
                }
                if (visible)
                    direct = lightCos / lightDistSq * LightIntensity / MathF.Max(lightP, 1e-9f);
            }
        }

        return reflectance * (ambient + direct);
    }

    /// <summary>
    /// The direct-lighting term at an indirect bounce, using uniform light
    /// selection with a shadow ray (mirrors the secondary/tertiary NEE in
    /// <c>TraceCore</c>: <c>cos / distSq * LightIntensity * nLights</c>).
    /// </summary>
    private static float UniformLightTerm(
        ISceneTracer tracer, ReadOnlySpan<Vector3> lights, int lightIdx,
        Vector3 hitPoint, Vector3 hitNormal)
    {
        Vector3 toLight = lights[lightIdx] - hitPoint;
        float distSq = Vector3.Dot(toLight, toLight);
        float dist = MathF.Sqrt(distSq);
        Vector3 lightDir = toLight / dist;
        float cosTheta = Vector3.Dot(hitNormal, lightDir);
        if (cosTheta <= 0f)
            return 0f;

        Vector3 shadowOrigin = hitPoint + hitNormal * 1e-3f;
        if (tracer.Occluded(shadowOrigin, lightDir, dist - 2e-3f))
            return 0f;

        return cosTheta / distSq * LightIntensity * lights.Length;
    }
}
