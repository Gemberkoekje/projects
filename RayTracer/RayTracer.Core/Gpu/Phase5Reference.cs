using System;
using System.Numerics;

namespace RayTracer;

/// <summary>
/// The debug views the GPU Phase 5 resolve pass can render, as a compact
/// contiguous code shared by the host (<c>Phase5Renderer</c>), the HLSL resolve
/// shader (<c>ResolvePhase5.hlsl</c>), and this reference. The codes are their own
/// stable numbering (not <see cref="DebugViewMode"/>'s ordinals) so reordering the
/// larger CPU enum can't silently repoint the shader switch.
///
/// This is the subset of <see cref="DebugViewMode"/> the GPU pipeline has the
/// buffers for today: the accumulation mean (beauty / direct / indirect splits),
/// the effective sample count, the G-buffer (depth / normal / albedo), and the
/// resolve-computed temporal signals (history weight / rejection). Variance and the
/// clamp heatmap are deferred to the Phase 5.2 statistics work, which adds the
/// Welford / clamp AOVs they need.
/// </summary>
public enum Phase5DebugMode : uint
{
    Beauty = 0,
    SampleCount = 1,
    Depth = 2,
    Normal = 3,
    Albedo = 4,
    DirectLighting = 5,
    IndirectLighting = 6,
    HistoryWeight = 7,
    RejectionMask = 8,
    // Added in Phase 5.2 (needs the Welford / clamp AOVs the statistics reduction
    // builds): the luma-variance heatmap and the per-pixel clamp heatmap.
    Variance = 9,
    ClampHeatmap = 10,
}

/// <summary>
/// The frame-aggregate statistics the Phase 5.2 on-GPU reduction produces and reads
/// back each frame (mirrors the subset of <see cref="FrameDiagnostics"/> the GPU
/// pipeline can compute). All averages/percentages are over every pixel, matching
/// the CPU renderer's <c>JobSystem.TaaResolver</c> reduction.
/// </summary>
public readonly record struct Phase5Stats(
    double AverageVariance,
    double AverageEffectiveSpp,
    double ClampedPixelPercent,
    double RejectedHistoryPercent,
    double AverageHistoryWeight,
    double HitPixelPercent,
    uint MaxObservedSampleCount);

/// <summary>
/// A CPU replica of the Phase 5 (debug views) GPU resolve pass's colorization: the
/// multi-stop palettes (sample count, depth, history weight, difference), the
/// normal/albedo encodings, and the per-mode dispatch. <c>ResolvePhase5.hlsl</c>
/// folds a line-for-line port of the methods here into its debug switch, so the
/// GPU views can be validated in plain C# where the GPU cannot run in CI.
///
/// The palette math mirrors the private helpers in
/// <c>JobSystem.DebugBufferRenderer</c> (<c>DebugBufferRenderer.cs</c>) — the same
/// stops in the same order — so a GPU debug view reads the same as the CPU app's.
/// Beauty / direct / indirect reuse <see cref="Phase1Reference.ResolveToSRGB"/>
/// (the XYZ→sRGB the resolve already uses), matching the CPU's <c>ColorFromXyz</c>.
/// </summary>
public static class Phase5Reference
{
    // ── Multi-stop gradient (mirrors DebugBufferRenderer.MultiStop) ────────

    private static Vector3 MultiStop(float t, Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 c4)
    {
        if (t <= 0.25f) return Vector3.Lerp(c0, c1, t / 0.25f);
        if (t <= 0.5f) return Vector3.Lerp(c1, c2, (t - 0.25f) / 0.25f);
        if (t <= 0.75f) return Vector3.Lerp(c2, c3, (t - 0.5f) / 0.25f);
        return Vector3.Lerp(c3, c4, (t - 0.75f) / 0.25f);
    }

    // ── Palettes (mirror DebugBufferRenderer.Palette*) ────────────────────

    public static Vector3 PaletteSampleCount(uint sampleCount, float maxSampleCount)
    {
        float t = maxSampleCount > 0f ? Math.Clamp(sampleCount / maxSampleCount, 0f, 1f) : 0f;
        return MultiStop(t,
            new Vector3(0.20f, 0.00f, 0.35f),
            new Vector3(0.00f, 0.70f, 1.00f),
            new Vector3(0.10f, 0.90f, 0.20f),
            new Vector3(1.00f, 0.95f, 0.20f),
            new Vector3(1.00f, 1.00f, 1.00f));
    }

    public static Vector3 PaletteHistoryWeight(float weight)
    {
        float t = Math.Clamp(weight, 0f, 1f);
        return MultiStop(t,
            new Vector3(0.10f, 0.10f, 0.20f),
            new Vector3(0.20f, 0.80f, 1.00f),
            new Vector3(1.00f, 0.95f, 0.20f),
            new Vector3(1.00f, 0.50f, 0.10f),
            new Vector3(1.00f, 0.10f, 0.10f));
    }

    public static Vector3 PaletteDepth(float depth)
    {
        float t = 1f - MathF.Exp(-depth * 0.08f);
        return MultiStop(t,
            new Vector3(0.02f, 0.02f, 0.05f),
            new Vector3(0.08f, 0.15f, 0.45f),
            new Vector3(0.10f, 0.60f, 0.80f),
            new Vector3(0.90f, 0.90f, 0.45f),
            new Vector3(1.00f, 1.00f, 1.00f));
    }

    public static Vector3 PaletteVariance(float variance, float maxVariance)
    {
        float t = maxVariance > 0f ? Math.Clamp(variance / maxVariance, 0f, 1f) : 0f;
        return MultiStop(t,
            new Vector3(0.00f, 0.00f, 0.20f),
            new Vector3(0.00f, 0.20f, 0.90f),
            new Vector3(0.00f, 0.80f, 0.30f),
            new Vector3(1.00f, 0.90f, 0.10f),
            new Vector3(1.00f, 0.20f, 0.10f));
    }

    public static Vector3 PaletteClamp(float amount)
    {
        float t = 1f - MathF.Exp(-amount * 0.5f);
        return MultiStop(t,
            new Vector3(0.00f, 0.00f, 0.00f),
            new Vector3(1.00f, 0.50f, 0.10f),
            new Vector3(1.00f, 0.15f, 0.10f),
            new Vector3(1.00f, 0.80f, 0.70f),
            new Vector3(1.00f, 1.00f, 1.00f));
    }

    /// <summary>Per-pixel luma variance from its Welford accumulator: the sample
    /// variance <c>M2/(count-1)</c>, 0 until the second sample (mirrors the CPU's
    /// <c>lumaVariance = count &gt; 1 ? lumaM2 / (count - 1) : 0</c>).</summary>
    public static float LumaVariance(float m2, uint count)
        => count > 1u ? m2 / (count - 1u) : 0f;

    public static Vector3 PaletteAlbedo(float albedo)
    {
        float v = Math.Clamp(albedo, 0f, 1f);
        return new Vector3(v, v, v);
    }

    public static Vector3 PaletteNormal(Vector3 normal)
    {
        if (normal == Vector3.Zero)
            return Vector3.Zero;
        Vector3 n = Vector3.Normalize(normal);
        return n * 0.5f + new Vector3(0.5f, 0.5f, 0.5f);
    }

    /// <summary>Green when history was reused, red when rejected
    /// (mirrors the <c>RejectionMask</c> branch in <c>RenderDebugModeToBuffer</c>).</summary>
    public static Vector3 PaletteRejection(bool rejected)
        => rejected ? new Vector3(1f, 0.1f, 0.1f) : new Vector3(0.1f, 0.9f, 0.1f);

    /// <summary>XYZ→sRGB for the color-valued views (beauty / lighting splits),
    /// matching the CPU's <c>ColorFromXyz</c> and the resolve shader's own
    /// <c>ResolveToSRGB</c>.</summary>
    public static Vector3 ColorFromXyz(Vector3 xyz) => Phase1Reference.ResolveToSRGB(xyz);

    // ── Per-mode dispatch (mirrors ResolvePhase5.hlsl's debug switch) ─────

    /// <summary>
    /// Colorizes one pixel for the selected debug view. <paramref name="surfaceWithFogXyz"/>
    /// is the resolved surface composited with this frame's fog (the Beauty XYZ);
    /// the other inputs are the per-pixel buffers the GPU resolve reads. The result
    /// is an unclamped color the caller saturates into 8-bit, exactly as the shader
    /// does with <c>saturate(...)</c>.
    /// </summary>
    public static Vector3 Colorize(
        Phase5DebugMode mode,
        Vector3 surfaceWithFogXyz,
        bool hit,
        uint sampleCount, float sampleCountNorm,
        float depth,
        Vector3 normal,
        float albedo,
        Vector3 directXyz,
        Vector3 indirectXyz,
        float historyWeight,
        bool rejected,
        float variance,
        float varianceNorm,
        float clampAmount)
    {
        return mode switch
        {
            Phase5DebugMode.SampleCount => PaletteSampleCount(sampleCount, sampleCountNorm),
            Phase5DebugMode.Depth => hit ? PaletteDepth(depth) : Vector3.Zero,
            Phase5DebugMode.Normal => PaletteNormal(normal),
            Phase5DebugMode.Albedo => PaletteAlbedo(albedo),
            Phase5DebugMode.DirectLighting => ColorFromXyz(directXyz),
            Phase5DebugMode.IndirectLighting => ColorFromXyz(indirectXyz),
            Phase5DebugMode.HistoryWeight => PaletteHistoryWeight(historyWeight),
            Phase5DebugMode.RejectionMask => PaletteRejection(rejected),
            Phase5DebugMode.Variance => PaletteVariance(variance, varianceNorm),
            Phase5DebugMode.ClampHeatmap => PaletteClamp(clampAmount),
            _ => ColorFromXyz(surfaceWithFogXyz), // Beauty
        };
    }

    /// <summary>
    /// The frame-statistics reduction the GPU <c>ReducePhase5.hlsl</c> pass performs,
    /// as pure C#: aggregate the per-pixel Welford variance, sample count, clamp
    /// flag, history weight, rejection flag and hit mask into the frame averages,
    /// percentages and the observed max sample count. A line-for-line mirror of the
    /// reduction at the tail of <c>JobSystem.TaaResolver.ResolveDisplayBufferWithTaa</c>,
    /// so the on-GPU reduction is testable in plain C#.
    /// </summary>
    public static Phase5Stats Reduce(
        ReadOnlySpan<float> lumaM2,
        ReadOnlySpan<uint> sampleCount,
        ReadOnlySpan<uint> clampHitFrame,
        ReadOnlySpan<uint> lastHit,
        ReadOnlySpan<float> historyWeight,
        ReadOnlySpan<uint> rejected,
        int pixels)
    {
        if (pixels <= 0)
            return default;

        double varianceSum = 0, sppSum = 0, histWeightSum = 0;
        long rejectedCount = 0, clampedCount = 0, hitCount = 0;
        uint maxSpp = 0;

        for (int i = 0; i < pixels; i++)
        {
            uint count = sampleCount[i];
            varianceSum += LumaVariance(lumaM2[i], count);
            sppSum += count;
            if (count > maxSpp)
                maxSpp = count;
            histWeightSum += historyWeight[i];
            if (rejected[i] != 0u) rejectedCount++;
            if (clampHitFrame[i] != 0u) clampedCount++;
            if (lastHit[i] != 0u) hitCount++;
        }

        return new Phase5Stats(
            AverageVariance: varianceSum / pixels,
            AverageEffectiveSpp: sppSum / pixels,
            ClampedPixelPercent: (double)clampedCount / pixels * 100.0,
            RejectedHistoryPercent: (double)rejectedCount / pixels * 100.0,
            AverageHistoryWeight: histWeightSum / pixels,
            HitPixelPercent: (double)hitCount / pixels * 100.0,
            MaxObservedSampleCount: maxSpp);
    }

    /// <summary>The Variance debug view's normalizer, matching the CPU legend's
    /// <c>Math.Max(0.0001, AverageVariance * 8)</c>.</summary>
    public static float VarianceViewNorm(double averageVariance)
        => (float)Math.Max(0.0001, averageVariance * 8.0);

    /// <summary>Parses a debug-view name (case-insensitive) for the windowed demo's
    /// <c>--debug &lt;name&gt;</c> option; returns <see cref="Phase5DebugMode.Beauty"/>
    /// for an unknown name.</summary>
    public static Phase5DebugMode ParseMode(string? name)
        => Enum.TryParse(name, ignoreCase: true, out Phase5DebugMode mode) ? mode : Phase5DebugMode.Beauty;
}
