using System.Collections.Generic;
using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase A of the shadows-and-caustics plan: transmittance-aware shadow rays. These pin the two
/// new primitives that let moving fog cast shadows and bubbles/glass cast soft, tinted shadows
/// instead of hard black discs — <see cref="BVH.Transmittance"/> (geometry) and
/// <see cref="JobSystem.SegmentTransmittance(Vector3,Vector3,Vector3,VolumetricOptions,bool)"/>
/// (participating medium) — plus the shared bubble reflect-probability and the preset wiring.
/// </summary>
[TestClass]
public sealed class ShadowTransmittanceTests
{
    private const int Wavelength = 550;

    private static SpectralData FlatSpectrum(float value)
    {
        var wl = new List<int>();
        var v = new List<float>();
        for (int w = 360; w <= 830; w += 5) { wl.Add(w); v.Add(value); }
        float[] a = v.ToArray();
        return new SpectralData(wl.ToArray(), a, (float[])a.Clone());
    }

    private static MaterialData DiffuseMat()
        => new("wall", "Wall", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            FlatSpectrum(0.5f), SurfaceKind.Diffuse);

    private static MaterialData BubbleMat()
        => new("bubble", "Bubble", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            FlatSpectrum(1.0f), SurfaceKind.Bubble, cauchyA: 1.33f, cauchyB: 0f, filmThicknessNm: 500f);

    private static MaterialData GlassMat()
        => new("glass", "Glass", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            FlatSpectrum(1.0f), SurfaceKind.Dielectric, cauchyA: 1.5f, cauchyB: 0f);

    private static Ray AlongZ()
        => new() { Origin = Vector3.Zero, Direction = Vector3.UnitZ, Wavelength = Wavelength, Intensity = 1f };

    // ── BVH.Transmittance: geometry ───────────────────────────────────────

    [TestMethod]
    public void Transmittance_ClearPath_IsOne()
    {
        // The only primitive sits off the ray, so nothing is hit.
        var bvh = new BVH([new Sphere(new Vector3(10f, 10f, 10f), 1f, DiffuseMat())]);
        Assert.AreEqual(1f, bvh.Transmittance(AlongZ(), 100f));
    }

    [TestMethod]
    public void Transmittance_OpaqueWall_IsZero()
    {
        var bvh = new BVH([new Sphere(new Vector3(0f, 0f, 5f), 1f, DiffuseMat())]);
        Assert.AreEqual(0f, bvh.Transmittance(AlongZ(), 100f));
    }

    [TestMethod]
    public void Transmittance_OpaqueBeyondMaxDist_IsNotBlocked()
    {
        // Wall at z≈49; a shadow ray whose light is only 10 units away must ignore it.
        var bvh = new BVH([new Sphere(new Vector3(0f, 0f, 50f), 1f, DiffuseMat())]);
        Assert.AreEqual(1f, bvh.Transmittance(AlongZ(), 10f));
    }

    [TestMethod]
    public void Transmittance_Bubble_PartiallyTransmitsPerReflectProbability()
    {
        var bubble = new Sphere(new Vector3(0f, 0f, 5f), 1f, BubbleMat());
        var bvh = new BVH([bubble]);
        var ray = AlongZ();

        HitInfo h = bubble.Intersect(ray).Value;
        float cos = MathF.Abs(Vector3.Dot(Vector3.Normalize(ray.Direction), h.Normal));
        float expected = 1f - Optics.BubbleReflectProbability(cos, h.Ior, h.Reflectance);

        float actual = bvh.Transmittance(ray, 100f);
        Assert.AreEqual(expected, actual, 1e-5f, "bubble transmittance == 1 − reflect probability");
        Assert.IsTrue(actual > 0f && actual < 1f, $"a bubble is a faint shadow, not opaque or invisible (T={actual})");
    }

    [TestMethod]
    public void Transmittance_Dielectric_PartiallyTransmitsPerFresnel()
    {
        var glass = new Sphere(new Vector3(0f, 0f, 5f), 1f, GlassMat());
        var bvh = new BVH([glass]);
        var ray = AlongZ();

        HitInfo h = glass.Intersect(ray).Value;
        float cos = MathF.Abs(Vector3.Dot(Vector3.Normalize(ray.Direction), h.Normal));
        float expected = 1f - Optics.FresnelDielectric(cos, 1f, h.Ior);

        float actual = bvh.Transmittance(ray, 100f);
        Assert.AreEqual(expected, actual, 1e-5f, "dielectric transmittance == 1 − Fresnel");
        Assert.IsTrue(actual > 0f && actual < 1f, $"glass casts a soft shadow (T={actual})");
    }

    [TestMethod]
    public void Transmittance_OpaqueBehindBubble_IsZero()
    {
        // Even after a bubble attenuates the ray, an opaque wall still fully blocks it.
        var bvh = new BVH(
        [
            new Sphere(new Vector3(0f, 0f, 5f), 1f, BubbleMat()),
            new Sphere(new Vector3(0f, 0f, 10f), 1f, DiffuseMat()),
        ]);
        Assert.AreEqual(0f, bvh.Transmittance(AlongZ(), 100f));
    }

    [TestMethod]
    public void Transmittance_OpaqueScene_MatchesBinaryOcclusion()
    {
        // The byte-identical guarantee: with only opaque geometry, Transmittance is exactly
        // 0 or 1 and agrees with !IsOccluded, so existing renders are unchanged.
        var bvh = new BVH(
        [
            new Sphere(new Vector3(0f, 0f, 5f), 1f, DiffuseMat()),
            new Sphere(new Vector3(3f, 0f, 5f), 1f, DiffuseMat()),
        ]);

        var blocked = AlongZ();
        var clear = new Ray { Origin = Vector3.Zero, Direction = Vector3.UnitX, Wavelength = Wavelength, Intensity = 1f };

        Assert.AreEqual(!bvh.IsOccluded(blocked, 100f) ? 1f : 0f, bvh.Transmittance(blocked, 100f), "blocked ray");
        Assert.AreEqual(!bvh.IsOccluded(clear, 2f) ? 1f : 0f, bvh.Transmittance(clear, 2f), "clear ray");
    }

    // ── Optics.BubbleReflectProbability (shared by the specular chain and shadows) ──

    [TestMethod]
    public void BubbleReflectProbability_MatchesBoostedFresnelTimesReflectance()
    {
        const float cos = 0.7f, ior = 1.33f, refl = 0.4f;
        float expected = Math.Clamp(Optics.BubbleReflectBoost * Optics.FresnelDielectric(cos, 1f, ior), 0f, 1f) * refl;
        Assert.AreEqual(expected, Optics.BubbleReflectProbability(cos, ior, refl), 1e-6f);
    }

    // ── JobSystem.SegmentTransmittance: participating medium ───────────────

    [TestMethod]
    public void SegmentTransmittance_ShadowTransmittanceOff_IsOne()
    {
        VolumetricOptions options = VolumetricOptions.FromQuality(VolumetricQuality.High, SmokeMode.AlwaysFog)
            with { ShadowTransmittance = false };

        float t = JobSystem.SegmentTransmittance(
            new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 8f), Vector3.UnitZ, options, isMoving: false);
        Assert.AreEqual(1f, t, "the opt-in is off → no fog shadowing, byte-identical to before");
    }

    [TestMethod]
    public void SegmentTransmittance_SmokeModeNone_IsOne()
    {
        VolumetricOptions options = VolumetricOptions.FromQuality(VolumetricQuality.High, SmokeMode.None);
        float t = JobSystem.SegmentTransmittance(
            Vector3.Zero, new Vector3(0f, 1f, 8f), Vector3.UnitZ, options, isMoving: false);
        Assert.AreEqual(1f, t);
    }

    [TestMethod]
    public void SegmentTransmittance_EnabledFog_AttenuatesAndDenserTransmitsLess()
    {
        var from = new Vector3(0f, 1f, 0f);
        var to = new Vector3(0f, 1f, 8f);
        // EarlyOutTransmittance is disabled so the comparison stays in the exact
        // (non-early-out) regime: with the optimization on, two results that both fall below
        // the 1% cutoff stop at different step counts and are not meaningfully ordered. Along a
        // fixed segment and density field, higher σ can only lower the transmittance integral.
        VolumetricOptions thin = VolumetricOptions.FromQuality(VolumetricQuality.High, SmokeMode.AlwaysFog)
            with { SigmaScaleFog = 1f, EarlyOutTransmittance = 0f };
        VolumetricOptions thick = thin with { SigmaScaleFog = 3f };

        float thinT = JobSystem.SegmentTransmittance(from, to, Vector3.UnitZ, thin, isMoving: false);
        float thickT = JobSystem.SegmentTransmittance(from, to, Vector3.UnitZ, thick, isMoving: false);

        Assert.IsTrue(thinT < 1f, $"fog on the shadow ray must dim the light (T={thinT})");
        Assert.IsTrue(thickT < thinT, $"denser fog casts a deeper shadow (thin={thinT}, thick={thickT})");
        Assert.IsTrue(thickT >= 0f, "transmittance stays non-negative");
    }

    // ── Preset wiring ──────────────────────────────────────────────────────

    [TestMethod]
    public void RenderPreset_NeeTiersEnableFogShadows_CheaperTiersDoNot()
    {
        Assert.IsTrue(RenderPreset.High.Volumetrics.ShadowTransmittance, "High is NEE → fog shadows on");
        Assert.IsTrue(RenderPreset.Ultra.Volumetrics.ShadowTransmittance, "Ultra is NEE → fog shadows on");
        Assert.IsFalse(RenderPreset.Medium.Volumetrics.ShadowTransmittance, "Medium is shadow-free Direct lighting");
        Assert.IsFalse(RenderPreset.Playable.Volumetrics.ShadowTransmittance);
        Assert.IsFalse(RenderPreset.Low.Volumetrics.ShadowTransmittance);
    }

    [TestMethod]
    public void RenderPreset_FogShadowDebug_IsNeeAlwaysFogWithShadows()
    {
        RenderPreset p = RenderPreset.FogShadowDebug;
        Assert.AreEqual(LightingMode.NEE, p.Lighting, "shadow rays must fire to show fog shadows");
        Assert.AreEqual(SmokeMode.AlwaysFog, p.Volumetrics.SmokeMode);
        Assert.IsTrue(p.Volumetrics.ShadowTransmittance);
        Assert.IsTrue(p.IsDebugSmokePreset);
    }
}
