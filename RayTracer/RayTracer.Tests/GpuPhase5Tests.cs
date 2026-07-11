using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 5 GPU-port parity tests. These validate the pure-C# debug colorization
/// replica (<see cref="Phase5Reference"/>) that <c>ResolvePhase5.hlsl</c> is a
/// line-for-line port of: the multi-stop palettes (sample count, depth, history
/// weight), the normal/albedo encodings, the rejection mask, and the per-mode
/// dispatch. The palette stops mirror <c>JobSystem.DebugBufferRenderer</c>, so a GPU
/// debug view reads the same as the CPU app's; the tests pin the endpoints and the
/// routing so a shader/reference divergence is caught in CI, where the GPU can't run.
/// (The on-hardware smoke test — every view renders TDR-free and distinctly — is the
/// <c>--phase5-selftest</c> path.)
/// </summary>
[TestClass]
public class GpuPhase5Tests
{
    private static void AssertVecClose(Vector3 expected, Vector3 actual, string what, float tol = 1e-5f)
    {
        Assert.IsTrue(
            MathF.Abs(expected.X - actual.X) <= tol &&
            MathF.Abs(expected.Y - actual.Y) <= tol &&
            MathF.Abs(expected.Z - actual.Z) <= tol,
            $"{what}: expected {expected}, got {actual}");
    }

    // ── Palette endpoints (mirror DebugBufferRenderer stops) ──────────────

    [TestMethod]
    public void PaletteSampleCount_SpansPurpleToWhite()
    {
        // t = 0 → first stop (deep purple); t ≥ 1 → last stop (white).
        AssertVecClose(new Vector3(0.20f, 0.00f, 0.35f), Phase5Reference.PaletteSampleCount(0, 100), "spp t=0");
        AssertVecClose(new Vector3(1f, 1f, 1f), Phase5Reference.PaletteSampleCount(100, 100), "spp t=1");
        AssertVecClose(new Vector3(1f, 1f, 1f), Phase5Reference.PaletteSampleCount(250, 100), "spp clamps above 1");
        // Zero normalizer must not divide by zero — falls back to t = 0.
        AssertVecClose(new Vector3(0.20f, 0.00f, 0.35f), Phase5Reference.PaletteSampleCount(5, 0), "spp norm=0");
    }

    [TestMethod]
    public void PaletteHistoryWeight_SpansDarkToRed()
    {
        AssertVecClose(new Vector3(0.10f, 0.10f, 0.20f), Phase5Reference.PaletteHistoryWeight(0f), "hw t=0");
        AssertVecClose(new Vector3(1.00f, 0.10f, 0.10f), Phase5Reference.PaletteHistoryWeight(1f), "hw t=1");
        AssertVecClose(new Vector3(1.00f, 0.10f, 0.10f), Phase5Reference.PaletteHistoryWeight(5f), "hw clamps");
    }

    [TestMethod]
    public void PaletteDepth_NearIsDarkFarSaturatesToWhite()
    {
        // depth 0 → first stop; the exponential ramp approaches the white last stop.
        AssertVecClose(new Vector3(0.02f, 0.02f, 0.05f), Phase5Reference.PaletteDepth(0f), "depth 0");
        Vector3 far = Phase5Reference.PaletteDepth(1000f);
        Assert.IsTrue(far.X > 0.99f && far.Y > 0.99f && far.Z > 0.99f, $"far depth should approach white, got {far}");
        // Monotone brightness with distance over the near range.
        Assert.IsTrue(Phase5Reference.PaletteDepth(20f).Length() > Phase5Reference.PaletteDepth(2f).Length(),
            "depth palette should brighten with distance");
    }

    [TestMethod]
    public void PaletteAlbedo_IsClampedGray()
    {
        AssertVecClose(new Vector3(0.5f, 0.5f, 0.5f), Phase5Reference.PaletteAlbedo(0.5f), "albedo mid");
        AssertVecClose(Vector3.Zero, Phase5Reference.PaletteAlbedo(-1f), "albedo clamps low");
        AssertVecClose(Vector3.One, Phase5Reference.PaletteAlbedo(2f), "albedo clamps high");
    }

    [TestMethod]
    public void PaletteNormal_EncodesUnitVectorAndZeroesEmpty()
    {
        AssertVecClose(new Vector3(0.5f, 0.5f, 1.0f), Phase5Reference.PaletteNormal(new Vector3(0, 0, 1)), "+Z normal");
        AssertVecClose(new Vector3(1.0f, 0.5f, 0.5f), Phase5Reference.PaletteNormal(new Vector3(1, 0, 0)), "+X normal");
        // Unnormalized input is normalized before encoding.
        AssertVecClose(new Vector3(0.5f, 0.5f, 1.0f), Phase5Reference.PaletteNormal(new Vector3(0, 0, 5)), "unnormalized");
        AssertVecClose(Vector3.Zero, Phase5Reference.PaletteNormal(Vector3.Zero), "zero normal");
    }

    [TestMethod]
    public void PaletteRejection_GreenReusedRedRejected()
    {
        AssertVecClose(new Vector3(0.1f, 0.9f, 0.1f), Phase5Reference.PaletteRejection(false), "reused=green");
        AssertVecClose(new Vector3(1f, 0.1f, 0.1f), Phase5Reference.PaletteRejection(true), "rejected=red");
    }

    [TestMethod]
    public void ColorFromXyz_MatchesResolveToSRGB()
    {
        var xyz = new Vector3(0.3f, 0.5f, 0.2f);
        AssertVecClose(Phase1Reference.ResolveToSRGB(xyz), Phase5Reference.ColorFromXyz(xyz), "color==sRGB resolve");
    }

    // ── Per-mode dispatch (mirrors ResolvePhase5.hlsl's debug switch) ─────

    [TestMethod]
    public void PaletteVariance_SpansDarkBlueToRed()
    {
        AssertVecClose(new Vector3(0.00f, 0.00f, 0.20f), Phase5Reference.PaletteVariance(0f, 1f), "var t=0");
        AssertVecClose(new Vector3(1.00f, 0.20f, 0.10f), Phase5Reference.PaletteVariance(1f, 1f), "var t=1");
        AssertVecClose(new Vector3(1.00f, 0.20f, 0.10f), Phase5Reference.PaletteVariance(5f, 1f), "var clamps");
        AssertVecClose(new Vector3(0.00f, 0.00f, 0.20f), Phase5Reference.PaletteVariance(3f, 0f), "var maxVar=0");
    }

    [TestMethod]
    public void PaletteClamp_ZeroIsBlackAndSaturatesToWhite()
    {
        AssertVecClose(Vector3.Zero, Phase5Reference.PaletteClamp(0f), "clamp 0");
        Vector3 hot = Phase5Reference.PaletteClamp(100f);
        Assert.IsTrue(hot.X > 0.99f && hot.Y > 0.99f && hot.Z > 0.99f, $"heavy clamp should approach white, got {hot}");
    }

    [TestMethod]
    public void LumaVariance_IsZeroUntilSecondSampleThenM2OverNMinus1()
    {
        Assert.AreEqual(0f, Phase5Reference.LumaVariance(3.2f, 0), "count 0");
        Assert.AreEqual(0f, Phase5Reference.LumaVariance(3.2f, 1), "count 1");
        Assert.AreEqual(3.0f / 4f, Phase5Reference.LumaVariance(3.0f, 5), 1e-6f, "M2/(count-1)");
    }

    [TestMethod]
    public void Colorize_RoutesEachModeToItsPalette()
    {
        var surface = new Vector3(0.4f, 0.6f, 0.3f);
        var normal = new Vector3(0.3f, 0.9f, -0.1f);
        var direct = new Vector3(0.2f, 0.3f, 0.1f);
        var indirect = new Vector3(0.05f, 0.02f, 0.08f);

        Vector3 Call(Phase5DebugMode mode, bool hit = true) => Phase5Reference.Colorize(
            mode, surface, hit,
            sampleCount: 7, sampleCountNorm: 20,
            depth: 4.5f, normal: normal, albedo: 0.42f,
            directXyz: direct, indirectXyz: indirect,
            historyWeight: 0.8f, rejected: true,
            variance: 0.03f, varianceNorm: 0.1f, clampAmount: 2.5f);

        AssertVecClose(Phase5Reference.ColorFromXyz(surface), Call(Phase5DebugMode.Beauty), "beauty");
        AssertVecClose(Phase5Reference.PaletteSampleCount(7, 20), Call(Phase5DebugMode.SampleCount), "spp");
        AssertVecClose(Phase5Reference.PaletteDepth(4.5f), Call(Phase5DebugMode.Depth), "depth hit");
        AssertVecClose(Vector3.Zero, Call(Phase5DebugMode.Depth, hit: false), "depth miss");
        AssertVecClose(Phase5Reference.PaletteNormal(normal), Call(Phase5DebugMode.Normal), "normal");
        AssertVecClose(Phase5Reference.PaletteAlbedo(0.42f), Call(Phase5DebugMode.Albedo), "albedo");
        AssertVecClose(Phase5Reference.ColorFromXyz(direct), Call(Phase5DebugMode.DirectLighting), "direct");
        AssertVecClose(Phase5Reference.ColorFromXyz(indirect), Call(Phase5DebugMode.IndirectLighting), "indirect");
        AssertVecClose(Phase5Reference.PaletteHistoryWeight(0.8f), Call(Phase5DebugMode.HistoryWeight), "history weight");
        AssertVecClose(Phase5Reference.PaletteRejection(true), Call(Phase5DebugMode.RejectionMask), "rejection");
        AssertVecClose(Phase5Reference.PaletteVariance(0.03f, 0.1f), Call(Phase5DebugMode.Variance), "variance");
        AssertVecClose(Phase5Reference.PaletteClamp(2.5f), Call(Phase5DebugMode.ClampHeatmap), "clamp heatmap");
    }

    // ── Statistics reduction (mirrors JobSystem.TaaResolver's reduction) ──

    [TestMethod]
    public void Reduce_ComputesFrameAggregates()
    {
        // 4 pixels with hand-chosen values so the aggregates are checkable by hand.
        var lumaM2 = new float[] { 0f, 4f, 9f, 2f };       // variance = M2/(count-1)
        var sampleCount = new uint[] { 1, 3, 4, 1 };        // variances: 0, 2, 3, 0
        var clampHitFrame = new uint[] { 0, 1, 1, 0 };      // 2 clamped
        var lastHit = new uint[] { 1, 1, 0, 1 };            // 3 hits
        var historyWeight = new float[] { 0f, 0.9f, 0.9f, 0f }; // sum 1.8
        var rejected = new uint[] { 1, 0, 0, 1 };           // 2 rejected

        Phase5Stats s = Phase5Reference.Reduce(
            lumaM2, sampleCount, clampHitFrame, lastHit, historyWeight, rejected, 4);

        Assert.AreEqual((0 + 2 + 3 + 0) / 4.0, s.AverageVariance, 1e-6, "avg variance");
        Assert.AreEqual((1 + 3 + 4 + 1) / 4.0, s.AverageEffectiveSpp, 1e-6, "avg spp");
        Assert.AreEqual(50.0, s.ClampedPixelPercent, 1e-6, "clamped %");
        Assert.AreEqual(50.0, s.RejectedHistoryPercent, 1e-6, "rejected %");
        Assert.AreEqual(1.8 / 4.0, s.AverageHistoryWeight, 1e-6, "avg history weight");
        Assert.AreEqual(75.0, s.HitPixelPercent, 1e-6, "hit %");
        Assert.AreEqual(4u, s.MaxObservedSampleCount, "max spp");
    }

    [TestMethod]
    public void Reduce_EmptyIsZero()
    {
        Phase5Stats s = Phase5Reference.Reduce(
            Array.Empty<float>(), Array.Empty<uint>(), Array.Empty<uint>(),
            Array.Empty<uint>(), Array.Empty<float>(), Array.Empty<uint>(), 0);
        Assert.AreEqual(default, s);
    }

    [TestMethod]
    public void VarianceViewNorm_MatchesCpuLegendRange()
    {
        Assert.AreEqual(0.0001f, Phase5Reference.VarianceViewNorm(0.0), 1e-9f, "floor");
        Assert.AreEqual(0.8f, Phase5Reference.VarianceViewNorm(0.1), 1e-6f, "8x average");
    }

    [TestMethod]
    public void ParseMode_IsCaseInsensitiveWithBeautyFallback()
    {
        Assert.AreEqual(Phase5DebugMode.Normal, Phase5Reference.ParseMode("normal"));
        Assert.AreEqual(Phase5DebugMode.SampleCount, Phase5Reference.ParseMode("SampleCount"));
        Assert.AreEqual(Phase5DebugMode.IndirectLighting, Phase5Reference.ParseMode("indirectlighting"));
        Assert.AreEqual(Phase5DebugMode.Beauty, Phase5Reference.ParseMode("nonsense"));
        Assert.AreEqual(Phase5DebugMode.Beauty, Phase5Reference.ParseMode(null));
    }
}
