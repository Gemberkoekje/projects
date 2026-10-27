using System.Diagnostics;
using Vista.Atmosphere;
using Vista.Output;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.Cli;

/// <summary>
/// What an estimate is of (vista-plan §10 M1.15): the final's scene on its own mesh, its settings, and what its preview and its detail
/// image need besides.
/// </summary>
/// <param name="Scene">The final's scene, on the final's mesh.</param>
/// <param name="Settings">The final's render settings.</param>
/// <param name="Frame">The scene's air frame.</param>
/// <param name="Surface">The world's surface (the detail image reads it).</param>
/// <param name="GroundRadius">The ground sphere's radius at the scene's base altitude, m.</param>
/// <param name="Forest">The forest's placement the meshes are made with.</param>
internal sealed record EstimateView(VistaScene Scene, RenderSettings Settings, AtmosphereFrame Frame, HeightField Surface, double GroundRadius,
    ForestPlacement Forest);

public sealed partial class PreviewSession
{
    /// <summary>
    /// The estimate of a final render (vista-plan §7.2, §10 M1.15, <c>--estimate</c>), run in place of it. Everything the final repeats is
    /// run as it will be and timed: the world, the pools, the auto camera, the mesh at the final's size, the device, the shaders, the
    /// upload, the acceleration structures, the air's tables, the exposure meter (all before this, in <see cref="Run"/>, or in the probe).
    /// The trace is projected from a probe: the final's own first passes over the whole frame, at its size and detail, with its outputs
    /// (so its video memory is the final's too), past <see cref="FinalEstimate.ProbeGpuSeconds"/> of GPU time until they reach the size
    /// the final's later passes keep. The files are timed by writing the probe's as the final writes its own (and removing them); the
    /// denoising from a centred crop of the probe. The preview at its own size gives the noise and the preview's own cost, the first
    /// calibration. The video memory is planned before anything is allocated: a final that would not fit is not probed at all.
    /// </summary>
    private (EstimateInfo Estimate, ExposureInfo Exposure) Estimate(PreviewRequest request, EstimateView view, Func<CameraSpec, TerrainMesh, VistaScene> sceneWith,
        SortedDictionary<PreviewStage, bool> stages, SortedDictionary<string, double> timings, List<string> warnings, Func<string, string> sibling)
    {
        IVistaRenderer renderer = _renderer!;
        VistaScene scene = view.Scene;
        RenderSettings settings = view.Settings;
        PixelWindow window = settings.Rendered;
        HashSet<Aov> aovs = AovsOf(request);
        var built = new HashSet<string>(StringComparer.Ordinal);
        bool rendered = false;
        var stopwatch = Stopwatch.StartNew();

        MemoryPlan plan = renderer.PlanMemory(scene, settings, aovs);
        if (plan.Warning() is { } warning)
        {
            warnings.Add(warning);
            Notice?.Invoke(warning);
        }
        if (!plan.Fits)
        {
            // Nothing of the final is allocated: it would be refused. The meter still runs, for the sidecar's exposure.
            ExposureInfo metered = MeterExposure(request, scene, settings, view.Frame, scene.Materials, stages, built, ref rendered);
            MarkRendererStages(stages, rendered, built, settings);
            return (new EstimateInfo(window.Width, window.Height, settings.SamplesPerPixel, new SortedDictionary<string, double>(StringComparer.Ordinal),
                TimeRange.Zero, null, null, null, null, null, null, null, MemoryInfo.Of(plan, 0), null), metered);
        }

        // The probe: the final's own first passes over the whole frame, on its mesh, with its outputs, until the planner stops.
        RenderSettings probing = settings with { Batching = settings.Batching with { StopAfterGpuSeconds = FinalEstimate.ProbeGpuSeconds } };
        var planner = new BatchPlanner(window.Width, window.Height, settings.SamplesPerPixel, probing.Batching);
        var clock = new TraceClock(planner, _time);
        RenderOutput probe = renderer.Render(scene, probing, aovs, planner);
        int probeSamples = planner.SamplesCompleted;
        probe = Rescaled(probe, (double)settings.SamplesPerPixel / probeSamples);
        built.UnionWith(probe.Stats.StageSeconds.Keys);
        rendered = true;
        stages[PreviewStage.Render] = false;
        timings["render"] = Lap(stopwatch);

        ExposureInfo exposure = MeterExposure(request, scene, settings, view.Frame, scene.Materials, stages, built, ref rendered);
        timings["exposureMeter"] = Lap(stopwatch);
        MarkRendererStages(stages, rendered, built, settings);
        SortedDictionary<string, double> setup = FinalEstimate.SetupStages(timings, probe.Stats.StageSeconds);
        if (StartupSeconds > 0.0)
            setup["startup"] = StartupSeconds;

        // The files, written as the final writes its own, then removed.
        float[] linear = DisplayTransform.XyzToLinearRec709(probe.Xyz);
        string ProbePath(string suffix) => sibling(".estimate-probe" + suffix);
        double beautySeconds;
        try
        {
            WriteImages(request, probe, linear, exposure, window, scene.Camera, scene.Sun, view.Surface, view.GroundRadius, ProbePath(".png"), ProbePath,
                string.Empty, null, [], out beautySeconds);
        }
        finally
        {
            timings["estimateWrite"] = Lap(stopwatch);
            foreach (string suffix in request.Debug.Select(d => $".{d.ToString().ToLowerInvariant()}.png").Append(".png").Append(".exr"))
                File.Delete(ProbePath(suffix));
        }
        TimeRange write = TimeRange.Around(timings["estimateWrite"], FinalEstimate.WriteMargin);
        stopwatch.Restart();

        TimeRange? denoise = null;
        if (request.Denoise is { } settingsOfDenoise && _denoiser is not null)
        {
            denoise = DenoiseFromCrop(probe, exposure, settingsOfDenoise, beautySeconds, warnings);
            timings["estimateDenoise"] = Lap(stopwatch);
        }

        var projection = TraceProjection.From(clock.Passes, window.Pixels, probe.Stats.RaysTraced);
        TimeRange trace = projection.WallSeconds(settings.SamplesPerPixel);
        TimeRange setupRange = TimeRange.Around(setup.Values.Sum(), FinalEstimate.SetupMargin);
        TimeRange total = FinalEstimate.Total(setupRange, trace, write, denoise);

        (NoiseInfo? noise, PreviewProbeInfo? preview) = PreviewProbe(request, view, sceneWith, exposure, projection, setupRange, write, denoise);
        timings["estimatePreview"] = Lap(stopwatch);

        var estimate = new EstimateInfo(window.Width, window.Height, settings.SamplesPerPixel, setup, setupRange,
            new ProbeInfo(probeSamples, [.. clock.Passes], probe.Stats.RaysTraced, clock.WallSeconds), projection, trace, write, denoise, total, noise,
            MemoryInfo.Of(plan, probe.Stats.PeakVideoMemoryBytes), preview);
        return (estimate, exposure);
    }

    /// <summary>
    /// The final's denoising, from a centred crop of the probe a <see cref="FinalEstimate.DenoiseCropDivisor"/>th of the frame on a side
    /// (vista-plan §10 M1.15): the filter's run scales with the pixels, its device and commit do not; the denoised PNG and EXR take what
    /// the beauty's did.
    /// </summary>
    private TimeRange? DenoiseFromCrop(RenderOutput probe, ExposureInfo exposure, DenoiseSettings settings, double beautySeconds, List<string> warnings)
    {
        int w = Math.Max(1, probe.Width / FinalEstimate.DenoiseCropDivisor), h = Math.Max(1, probe.Height / FinalEstimate.DenoiseCropDivisor);
        var crop = new PixelWindow((probe.Width - w) / 2, (probe.Height - h) / 2, w, h);
        RenderOutput cropped = Crop(probe, crop);
        var stopwatch = Stopwatch.StartNew();
        DenoiseInput input = Denoising.Prepare(cropped, DisplayTransform.XyzToLinearRec709(cropped.Xyz), Denoising.InputScale(settings, exposure), out _);
        DenoiseResult result = _denoiser!.Denoise(input, settings);
        double seconds = stopwatch.Elapsed.TotalSeconds;
        if (!result.Succeeded || result.Run is not { } run)
        {
            warnings.Add($"Not denoised in the estimate: {result.Problem} The final would write the raw image only.");
            return null;
        }
        double scale = (double)probe.Width * probe.Height / crop.Pixels;
        return TimeRange.Around(seconds - run.ExecuteSeconds + run.ExecuteSeconds * scale + beautySeconds, FinalEstimate.DenoiseMargin);
    }

    /// <summary>
    /// A probe's image as the means over the samples it traced: a stopped render divided its sums by the samples asked for, so its colour
    /// and albedo are <paramref name="factor"/> (asked over traced) too dark; the normal, the first hit and the variance are left as they are.
    /// </summary>
    internal static RenderOutput Rescaled(RenderOutput output, double factor)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (factor == 1.0)
            return output;
        float[] Scaled(float[] data) => [.. data.Select(v => (float)(v * factor))];
        var aovs = output.Aovs.ToDictionary(a => a.Key, a => a.Key == Aov.Albedo ? Scaled(a.Value) : a.Value);
        return output with { Xyz = Scaled(output.Xyz), Aovs = aovs };
    }

    /// <summary>The <paramref name="window"/> of <paramref name="output"/>: its colour and every output it has.</summary>
    internal static RenderOutput Crop(RenderOutput output, PixelWindow window)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!window.FitsIn(output.Width, output.Height))
            throw new ArgumentException($"Window {window} does not fit the {output.Width}×{output.Height} image.", nameof(window));
        float[] Cut(float[] data, int channels)
        {
            var cut = new float[window.Pixels * channels];
            for (int y = 0; y < window.Height; y++)
                Array.Copy(data, ((window.Y + y) * output.Width + window.X) * channels, cut, y * window.Width * channels, window.Width * channels);
            return cut;
        }
        return new RenderOutput(window.Width, window.Height, Cut(output.Xyz, 3),
            output.Aovs.ToDictionary(a => a.Key, a => Cut(a.Value, AovLayout.Channels(a.Key))), output.Stats);
    }

    /// <summary>
    /// The preview of the final's view at the preview's own size, mesh and samples (at least 64, so each pixel's variance holds): its noise,
    /// carried to the final's samples and to a noise target (<see cref="NoiseMeter"/>), and its own cost per pixel-sample against the probe's,
    /// the first calibration (vista-plan §7.2).
    /// </summary>
    private (NoiseInfo Noise, PreviewProbeInfo Preview) PreviewProbe(PreviewRequest request, EstimateView view, Func<CameraSpec, TerrainMesh, VistaScene> sceneWith,
        ExposureInfo exposure, TraceProjection projection, TimeRange setup, TimeRange write, TimeRange? denoise)
    {
        RenderSettings final = view.Settings;
        int width = Math.Min(PreviewCommandLine.DefaultWidth, final.Width);
        int height = Math.Max(1, (int)Math.Round((double)width * final.Height / final.Width, MidpointRounding.AwayFromZero));
        int samples = Math.Clamp(final.SamplesPerPixel, 64, 1024);
        TerrainMesh mesh = _worlds.Mesh(MeshView.For(view.Scene.Camera, width, height, view.GroundRadius), request.Mesh with { Forest = view.Forest });
        RenderSettings settings = final with { Width = width, Height = height, SamplesPerPixel = samples, Window = default };
        RenderOutput preview = _renderer!.Render(sceneWith(view.Scene.Camera, mesh), settings, new HashSet<Aov> { Aov.Variance },
            new BatchPlanner(width, height, samples, settings.Batching));

        float[] steps = NoiseMeter.OneSampleSteps(DisplayTransform.XyzToLinearRec709(preview.Xyz), preview.Xyz, preview.Aovs[Aov.Variance], samples,
            exposure.AppliedEv, request.Display, request.Look);
        double oneSample = NoiseMeter.PercentileOf(steps, NoiseMeter.Percentile);
        int? target = null, targetLow = null;
        TimeRange? targetTrace = null, targetTotal = null;
        if (request.NoiseTarget is { } goal)
        {
            target = NoiseMeter.SamplesFor(oneSample, goal);
            targetLow = Math.Max(1, (int)Math.Floor(target.Value * NoiseMeter.FinalShareLow));
            TimeRange high = projection.WallSeconds(target.Value), low = projection.WallSeconds(targetLow.Value);
            targetTrace = new TimeRange(low.Low, high.Mid, high.High);
            targetTotal = FinalEstimate.Total(setup, targetTrace.Value, write, denoise);
        }
        var noise = new NoiseInfo(samples, NoiseMeter.Percentile, oneSample, NoiseMeter.StepsAt(oneSample, final.SamplesPerPixel), request.NoiseTarget,
            target, targetLow, targetTrace, targetTotal);

        RenderStats s = preview.Stats;
        double cost = s.PixelSamples > 0 ? s.TraceSeconds * 1e9 / s.PixelSamples : 0.0;
        var info = new PreviewProbeInfo(width, height, samples, mesh.TriangleCount, cost, s.RaysPerSample(), s.RaysPerSecond(),
            cost > 0.0 ? projection.CostNs / cost : 0.0);
        return (noise, info);
    }
}
