using Vista.Rendering;

namespace Vista.Output;

/// <summary>
/// A render's video memory as planned before it and as measured during it (schema 26, vista-plan §7.1, §10 M1.15): the plan's base,
/// its buffers by name and its peak, DXGI's budget, and the peak the render took.
/// </summary>
/// <param name="BaseBytes">The process's video memory before its first render (the device, its pipelines, its fixed tables).</param>
/// <param name="BudgetBytes">DXGI's budget for the process when the plan was made.</param>
/// <param name="PlannedPeakBytes">The plan's peak, the base included.</param>
/// <param name="PeakStage">The stage the plan peaks in.</param>
/// <param name="PlannedBytes">The plan's buffers by name, summed (each rounded up to 64 KiB).</param>
/// <param name="Fits">True when the planned peak is within the budget.</param>
/// <param name="Tight">True when it passes <see cref="MemoryPlan.TightShare"/> of the budget.</param>
/// <param name="MeasuredPeakBytes">The most the process held during the render; 0 when it did not run.</param>
/// <param name="PlanError">The plan's peak against the measured one, as a share of the measured (+0.02: planned 2% high); null without a measurement.</param>
public sealed record MemoryInfo(long BaseBytes, long BudgetBytes, long PlannedPeakBytes, MemoryStage PeakStage, SortedDictionary<string, long> PlannedBytes,
    bool Fits, bool Tight, long MeasuredPeakBytes, double? PlanError)
{
    /// <summary>The record of <paramref name="plan"/>, with the peak a render measured (0 when none ran).</summary>
    public static MemoryInfo Of(MemoryPlan plan, long measuredPeakBytes)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var bytes = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (MemoryItem item in plan.Items)
            bytes[item.Name] = bytes.GetValueOrDefault(item.Name) + item.Bytes;
        double? error = measuredPeakBytes > 0 ? (double)(plan.PeakBytes - measuredPeakBytes) / measuredPeakBytes : null;
        return new MemoryInfo(plan.BaseBytes, plan.BudgetBytes, plan.PeakBytes, plan.PeakStage, bytes, plan.Fits, plan.Tight, measuredPeakBytes, error);
    }
}

/// <summary>
/// A trace's own clock (schema 26, vista-plan §10 M1.15): its wall and GPU seconds, its passes, and what the live ETA predicted at
/// a few points of it against what was in fact left.
/// </summary>
/// <param name="WallSeconds">Wall seconds from the first dispatch issued to the last completed.</param>
/// <param name="GpuSeconds">GPU seconds of the dispatches.</param>
/// <param name="Passes">Passes over the frame.</param>
/// <param name="Checkpoints">The ETA at 10%, 25%, 50%, 75% and 90% of the pixel-samples.</param>
public sealed record TraceInfo(double WallSeconds, double GpuSeconds, int Passes, IReadOnlyList<EtaCheckpoint> Checkpoints)
{
    /// <summary>The record of <paramref name="clock"/>'s trace.</summary>
    public static TraceInfo Of(TraceClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new TraceInfo(clock.WallSeconds, clock.GpuSeconds, clock.Passes.Count, [.. clock.Checkpoints]);
    }
}

/// <summary>The probe an estimate traced at the final's size and detail (vista-plan §10 M1.15): whole-frame passes on the final's mesh.</summary>
/// <param name="SamplesPerPixel">Samples per pixel it traced.</param>
/// <param name="Passes">Its passes, in order.</param>
/// <param name="RaysTraced">Rays it traced.</param>
/// <param name="TraceSeconds">Its trace's wall seconds.</param>
public sealed record ProbeInfo(int SamplesPerPixel, IReadOnlyList<PassTiming> Passes, ulong RaysTraced, double TraceSeconds);

/// <summary>
/// The preview an estimate rendered (vista-plan §7.2's first calibration, §10 M1.15): at the preview's size, on the preview's mesh,
/// at the preview's samples; its cost against the probe's says how far a preview alone would have been off.
/// </summary>
/// <param name="Width">Preview width.</param>
/// <param name="Height">Preview height.</param>
/// <param name="SamplesPerPixel">Its samples per pixel.</param>
/// <param name="Triangles">Its mesh's triangles.</param>
/// <param name="CostNs">GPU nanoseconds per pixel-sample.</param>
/// <param name="RaysPerSample">Rays per pixel-sample.</param>
/// <param name="RaysPerSecond">Rays per GPU second.</param>
/// <param name="FinalCostRatio">The probe's cost per pixel-sample over the preview's.</param>
public sealed record PreviewProbeInfo(int Width, int Height, int SamplesPerPixel, long Triangles, double CostNs, double RaysPerSample, double RaysPerSecond,
    double FinalCostRatio);

/// <summary>
/// The final's noise in steps of the 8-bit PNG, from the preview's variance (vista-plan §7.2's noise target, <see cref="NoiseMeter"/>).
/// </summary>
/// <param name="PreviewSamples">The samples per pixel the preview's variance was measured over.</param>
/// <param name="Percentile">The share of the frame's pixels the numbers hold for.</param>
/// <param name="OneSampleSteps">The frame's noise after one sample, steps.</param>
/// <param name="StepsAtSamples">The noise at the final's samples per pixel, steps.</param>
/// <param name="TargetSteps">The noise target asked for (<c>--noise</c>); null without one.</param>
/// <param name="SamplesForTarget">The samples per pixel the preview says meet the target; null without one.</param>
/// <param name="SamplesForTargetLow">The low end of the range for the final's own pixels (<see cref="NoiseMeter.FinalShareLow"/>).</param>
/// <param name="TraceForTarget">The trace's wall time at the high end of that range; null without a target.</param>
/// <param name="TotalForTarget">The whole run's at those samples; null without a target.</param>
public sealed record NoiseInfo(int PreviewSamples, double Percentile, double OneSampleSteps, double StepsAtSamples, double? TargetSteps,
    int? SamplesForTarget, int? SamplesForTargetLow, TimeRange? TraceForTarget, TimeRange? TotalForTarget);

/// <summary>
/// What a final render will take (schema 26, vista-plan §7.2, §10 M1.15, <c>--estimate</c>): the stages it repeats, measured by
/// running them; its trace, projected from a probe at its own size and detail; its files' writing, measured on the probe; its
/// denoising, from a crop; their sum as a range; the noise it will show; and its video memory, planned and measured.
/// </summary>
/// <param name="Width">The final's width.</param>
/// <param name="Height">The final's height.</param>
/// <param name="SamplesPerPixel">The final's samples per pixel.</param>
/// <param name="SetupSeconds">The stages the final repeats before and around its trace, as the estimate measured them.</param>
/// <param name="Setup">Their sum, as a range.</param>
/// <param name="Probe">The probe at the final's size and detail.</param>
/// <param name="Projection">The trace's projection from it.</param>
/// <param name="Trace">The final's trace, wall seconds.</param>
/// <param name="Write">The final's files, wall seconds.</param>
/// <param name="Denoise">The final's denoising, wall seconds; null when it does not denoise.</param>
/// <param name="Total">The whole run, from the process's start to its last file.</param>
/// <param name="Noise">The final's noise; null when the preview did not run.</param>
/// <param name="Memory">The final's video memory.</param>
/// <param name="Preview">The preview's own cost; null when it did not run.</param>
public sealed record EstimateInfo(int Width, int Height, int SamplesPerPixel, SortedDictionary<string, double> SetupSeconds, TimeRange Setup,
    ProbeInfo? Probe, TraceProjection? Projection, TimeRange? Trace, TimeRange? Write, TimeRange? Denoise, TimeRange? Total, NoiseInfo? Noise,
    MemoryInfo Memory, PreviewProbeInfo? Preview);
