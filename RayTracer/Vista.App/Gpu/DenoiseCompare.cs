using System.Globalization;
using Vista.App.Denoise;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --denoise-compare</c> (vista-plan §10 M1.6 verify): one view rendered at <c>--spp</c> (the image anyone would see,
/// random stream 0) and a reference at <c>--reference-spp</c> (two independent halves, so its own noise can be taken out of the
/// error, <see cref="ImageError.RelativeMse"/>), then the low-sample render denoised every way the settings allow: albedo and
/// normal as noisy, clean or prefiltered, the input scale automatic, physical or metered, each quality, on each device. For every
/// way: the error against the reference (relMSE, and the samples per pixel the raw render would need for it), the display error,
/// the change in mean brightness, the time, and whether denoising the same input twice gives the same bits. A loupe of the input
/// denoised alone is compared with the same window of the denoised frame. <c>--timing-width</c> also times the default settings
/// on each device at that width (a 4K final).
/// <code>
/// Vista.App --denoise-compare --recipe r.json [--camera c.json] [--seed 42] [--width 640] [--spp 16] [--reference-spp 4096]
///                             [--bounces 6] [--devices cpu,cuda] [--loupe x,y,w,h] [--timing-width 3840] [--out dir]
/// </code>
/// With <c>--out</c> it writes the raw, denoised and reference images (one exposure, the meter's), every way's denoised image, a
/// raw | denoised | reference sheet with a magnified loupe of each below, and the numbers.
/// </summary>
internal static class DenoiseCompare
{
    /// <summary>The flag that selects this mode.</summary>
    public const string Flag = "--denoise-compare";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --denoise-compare --recipe r.json [--camera c.json] [--seed 42] [--width 640] [--spp 16] [--reference-spp 4096]\n" +
        "                 [--bounces 6] [--devices cpu,cuda] [--loupe x,y,w,h] [--timing-width 3840] [--out dir]\n" +
        "                 (a low-sample render denoised every way against a reference: error, cost, determinism)";

    /// <summary>Random streams of the reference's two halves (stream 0 is the image's own).</summary>
    private const uint ReferenceStreams = 1;

    /// <summary>Timed runs per device at <c>--timing-width</c>: the first, then the median of the rest.</summary>
    private const int TimingRuns = 4;

    /// <summary>Runs the comparison; returns 0, or 1 for bad arguments or a failed default denoise.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        var problems = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] known = ["--recipe", "--camera", "--seed", "--width", "--spp", "--reference-spp", "--bounces", "--devices", "--loupe", "--timing-width", "--out"];
        for (int i = 1; i < args.Count; i++)
        {
            if (i + 1 < args.Count && known.Contains(args[i], StringComparer.Ordinal))
                values[args[i]] = args[++i];
            else
                problems.Add($"Unknown or valueless argument '{args[i]}'.");
        }
        if (!values.ContainsKey("--recipe"))
            problems.Add("--recipe is required.");
        ulong seed = values.TryGetValue("--seed", out string? s) && ulong.TryParse(s, CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 42;
        int width = Int(values, "--width", 640, problems);
        int spp = Int(values, "--spp", 16, problems);
        int referenceSpp = Int(values, "--reference-spp", 4096, problems);
        int bounces = Int(values, "--bounces", new RenderSettings { Width = 1, Height = 1 }.MaxBounces, problems);
        int timingWidth = values.ContainsKey("--timing-width") ? Int(values, "--timing-width", 3840, problems) : 0;
        if (referenceSpp < 2 || referenceSpp % 2 != 0)
            problems.Add("--reference-spp must be even (two halves).");
        var devices = new List<DenoiseDevice>();
        foreach (string part in values.GetValueOrDefault("--devices", "cpu,cuda").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse(part, ignoreCase: true, out DenoiseDevice device) && device != DenoiseDevice.None && Enum.IsDefined(device) && !int.TryParse(part, out _))
                devices.Add(device);
            else
                problems.Add($"--devices takes cpu and cuda, got '{part}'.");
        }
        if (!devices.Contains(DenoiseDevice.Cpu))
            devices.Insert(0, DenoiseDevice.Cpu); // every way runs on the CPU, the device the others are compared with
        int height = Math.Max(1, (int)Math.Round(width * 9.0 / 16.0));
        var loupe = new PixelWindow(width * 3 / 8, height * 3 / 8, width / 4, height / 4);
        if (values.TryGetValue("--loupe", out string? loupeText) && (!PixelWindow.TryParse(loupeText, out loupe) || !loupe.FitsIn(width, height)))
            problems.Add($"--loupe must be x,y,w,h inside the {width}×{height} frame, got '{loupeText}'.");
        if (problems.Count > 0)
        {
            foreach (string problem in problems)
                Console.Error.WriteLine(problem);
            Console.Error.WriteLine(Usage);
            return 1;
        }

        Recipe recipe = VistaJson.ParseRecipe(File.ReadAllText(values["--recipe"]));
        var worlds = new WorldCache();
        HeightField terrain = worlds.World(recipe, seed, WorldGrid.Default, erode: false).Surface;
        CameraSpec camera = values.TryGetValue("--camera", out string? cameraPath) ? VistaJson.ParseCamera(File.ReadAllText(cameraPath)) : DefaultCamera.For(terrain);
        Almanac almanac = Almanac.For(recipe);
        (double ground, string groundFrom) = Places.BaseAltitude(recipe, almanac.Place);
        TerrainMesh mesh = worlds.Mesh(MeshView.For(camera, width, height, AtmosphereModel.Default.PlanetRadiusMetres + ground), new MeshSettings());
        var scene = new VistaScene(recipe, seed, camera, terrain, mesh, almanac.Sun, MaterialCatalog.M0, AtmosphereModel.Default, ground);

        using var renderer = new D3D12VistaRenderer();
        using var denoiser = new OidnDenoiser();
        log.WriteLine($"Device: {renderer.DeviceName}");
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{recipe.Name ?? "recipe"}, seed {seed}, {width}×{height}, {spp} spp against {referenceSpp} spp (two halves), {bounces} bounces; " +
            $"sun {almanac.Sun.ElevationDegrees:0.0}° up at {almanac.Sun.AzimuthDegrees:0}°; base {ground:0} m ({groundFrom})"));

        var settings = new RenderSettings { Width = width, Height = height, SamplesPerPixel = spp, MaxBounces = bounces };
        RenderOutput raw = Render(renderer, scene, settings, [Aov.Albedo, Aov.Normal]);
        RenderSettings meter = AutoExposure.MeterSettings(settings);
        ExposureInfo exposure = AutoExposure.Measure(Render(renderer, scene, meter, []).Xyz, camera.ExposureEv);
        var half = settings with { SamplesPerPixel = referenceSpp / 2 };
        float[] halfA = DisplayTransform.XyzToLinearRec709(Render(renderer, scene, half with { RandomStream = ReferenceStreams }, []).Xyz);
        float[] halfB = DisplayTransform.XyzToLinearRec709(Render(renderer, scene, half with { RandomStream = ReferenceStreams + 1 }, []).Xyz);
        float[] reference = [.. halfA.Select((a, i) => 0.5f * (a + halfB[i]))];
        float[] rawLinear = DisplayTransform.XyzToLinearRec709(raw.Xyz);
        double ev = exposure.AppliedEv;
        double scale = Math.Pow(2.0, ev);
        RelativeError rawError = ImageError.RelativeMse(rawLinear, halfA, halfB, scale);
        byte[] rawRgb = DisplayTransform.ToSrgb8(rawLinear, ev, DisplayTransform.DefaultCurve);
        byte[] referenceRgb = DisplayTransform.ToSrgb8(reference, ev, DisplayTransform.DefaultCurve);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  exposure {ev:+0.00;-0.00} EV (metered {exposure.Average!.LogAverageLuminanceCdM2:G3} cd/m²); reference noise relMSE {rawError.ReferenceNoise:G3} " +
            $"({referenceSpp} spp); raw {spp} spp: relMSE {rawError.Corrected:0.00000}, display RMSE {ImageError.DisplayRmse(rawRgb, referenceRgb):0.00}, " +
            $"mean {ImageError.MeanLuminanceBias(rawLinear, reference):+0.000%;-0.000%;0.000%}"));

        // Every way to denoise: the auxiliary images × the input scale at high quality on the CPU, then the other qualities and
        // the other devices at the defaults.
        var defaults = new DenoiseSettings();
        var ways = new List<DenoiseSettings>();
        foreach (DenoiseAuxiliary aux in new[] { DenoiseAuxiliary.Noisy, DenoiseAuxiliary.Clean, DenoiseAuxiliary.Prefiltered })
        {
            foreach (DenoiseInputScale inputScale in new[] { DenoiseInputScale.Auto, DenoiseInputScale.Physical, DenoiseInputScale.Metered })
                ways.Add(defaults with { Device = DenoiseDevice.Cpu, Quality = DenoiseQuality.High, Auxiliary = aux, InputScale = inputScale });
        }
        foreach (DenoiseQuality quality in new[] { DenoiseQuality.Balanced, DenoiseQuality.Fast })
            ways.Add(defaults with { Device = DenoiseDevice.Cpu, Quality = quality });
        foreach (DenoiseDevice device in devices.Where(d => d != DenoiseDevice.Cpu))
        {
            foreach (DenoiseQuality quality in new[] { DenoiseQuality.High, DenoiseQuality.Balanced, DenoiseQuality.Fast })
                ways.Add(defaults with { Device = device, Quality = quality });
        }

        log.WriteLine("  device  quality   aux          scale     relMSE     ≈ raw spp  display RMSE  mean      twice     prepare ms  filter ms  vs CPU (max rel.)");
        var rows = new List<DenoiseCompareRow>();
        var failures = new List<DenoiseFailure>();
        var images = new Dictionary<DenoiseSettings, float[]>();
        foreach (DenoiseSettings way in ways)
        {
            DenoiseInput input = Denoising.Prepare(raw, rawLinear, Denoising.InputScale(way, exposure), out long clamped);
            DenoiseResult first = denoiser.Denoise(input, way);
            DenoiseResult second = first.Succeeded ? denoiser.Denoise(input, way) : first;
            if (!first.Succeeded || !second.Succeeded)
            {
                var failure = new DenoiseFailure(way, first.Problem.Length > 0 ? first.Problem : second.Problem);
                failures.Add(failure);
                log.WriteLine($"  {Name(way)}  not denoised: {failure.Problem}");
                continue;
            }
            images[way] = first.Color;
            RelativeError error = ImageError.RelativeMse(first.Color, halfA, halfB, scale);
            double equal = ImageError.EqualErrorSpp(spp, rawError.Corrected, error.Corrected);
            float[]? cpu = images.GetValueOrDefault(way with { Device = DenoiseDevice.Cpu });
            double? fromCpu = way.Device != DenoiseDevice.Cpu && cpu is not null ? ImageError.MaxRelativeDifference(first.Color, cpu) : null;
            var row = new DenoiseCompareRow(way, error.Corrected, double.IsFinite(equal) ? equal : null,
                ImageError.DisplayRmse(DisplayTransform.ToSrgb8(first.Color, ev, DisplayTransform.DefaultCurve), referenceRgb), ImageError.MeanLuminanceBias(first.Color, reference),
                ImageError.BitIdentical(first.Color, second.Color), first.Run!.CommitSeconds * 1e3, second.Run!.ExecuteSeconds * 1e3, fromCpu, clamped,
                first.Run);
            rows.Add(row);
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {Name(way)}  {row.RelativeMse,9:0.000000}  {(row.EqualErrorSpp is double n ? n.ToString("0", CultureInfo.InvariantCulture) : "beyond"),9}  " +
                $"{row.DisplayRmse,12:0.00}  {row.MeanBias,8:+0.00%;-0.00%;0.00%}  {(row.BitIdentical ? "identical" : "DIFFERENT"),-9} {row.CommitMs,10:0.0}  " +
                $"{row.ExecuteMs,9:0.0}  {(row.MaxRelativeDifferenceFromCpu is double d ? d.ToString("0.0000", CultureInfo.InvariantCulture) : "-")}"));
        }
        DenoiseCompareRow? chosen = rows.FirstOrDefault(r => r.Settings == defaults);
        if (chosen is null)
        {
            log.WriteLine($"  the default settings did not denoise: {failures.First(f => f.Settings == defaults).Problem}");
            return 1;
        }
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  colour values given to the filter as 0 (negative or not finite): {chosen.ClampedValues} of {width * height * 3}"));

        // A loupe denoised alone against the same window of the denoised frame: the filter sees no pixels outside the window.
        LoupeCheck loupeCheck = Loupe(denoiser, raw, rawLinear, exposure, defaults, loupe, images[defaults], ev);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  loupe {loupe} denoised alone against that window of the denoised frame: display RMSE {loupeCheck.DisplayRmse:0.00}, " +
            $"max relative difference {loupeCheck.MaxRelativeDifference:0.000}; with the automatic scale {loupeCheck.AutoDisplayRmse:0.00}"));

        var timings = new List<DenoiseTiming>();
        if (timingWidth > 0)
            timings = Timings(renderer, denoiser, scene, settings, timingWidth, devices, defaults, log);

        if (values.TryGetValue("--out", out string? outDir))
        {
            string waysDir = Path.Combine(outDir, "ways");
            Directory.CreateDirectory(waysDir);
            foreach ((DenoiseSettings way, float[] image) in images)
                PngEncoder.Write(Path.Combine(waysDir, FileName(way) + ".png"), DisplayTransform.ToSrgb8(image, ev, DisplayTransform.DefaultCurve), width, height, PngColor.Rgb);
            byte[] denoisedRgb = DisplayTransform.ToSrgb8(images[defaults], ev, DisplayTransform.DefaultCurve);
            PngEncoder.Write(Path.Combine(outDir, "raw.png"), rawRgb, width, height, PngColor.Rgb);
            PngEncoder.Write(Path.Combine(outDir, "denoised.png"), denoisedRgb, width, height, PngColor.Rgb);
            PngEncoder.Write(Path.Combine(outDir, "reference.png"), referenceRgb, width, height, PngColor.Rgb);
            ExrEncoder.WriteRgb(Path.Combine(outDir, "raw.exr"), rawLinear, width, height);
            ExrEncoder.WriteRgb(Path.Combine(outDir, "denoised.exr"), images[defaults], width, height);
            ExrEncoder.WriteRgb(Path.Combine(outDir, "reference.exr"), reference, width, height);

            int factor = Math.Max(1, Math.Min(width / loupe.Width, height / loupe.Height));
            byte[] Magnified(byte[] rgb) => ContactSheet.Magnify(Crop8(rgb, width, loupe), loupe.Width, loupe.Height, factor);
            var tiles = new List<byte[]> { rawRgb, denoisedRgb, referenceRgb, Magnified(rawRgb), Magnified(denoisedRgb), Magnified(referenceRgb) };
            var sizes = new List<(int, int)> { (width, height), (width, height), (width, height) };
            sizes.AddRange(Enumerable.Repeat((loupe.Width * factor, loupe.Height * factor), 3));
            string asRaw = chosen.EqualErrorSpp is double n ? n.ToString("0", CultureInfo.InvariantCulture) : "beyond the reference's";
            string[] labels =
            [
                string.Create(CultureInfo.InvariantCulture, $"raw, {spp} spp: relMSE {rawError.Corrected:0.0000}"),
                string.Create(CultureInfo.InvariantCulture,
                    $"denoised ({chosen.Run.Device.Split(':')[0]}, {Describe(defaults)}): relMSE {chosen.RelativeMse:0.0000}, as raw {asRaw} spp"),
                $"reference, {referenceSpp} spp",
                $"loupe {loupe} ×{factor}", $"loupe {loupe} ×{factor}", $"loupe {loupe} ×{factor}",
            ];
            string title = string.Create(CultureInfo.InvariantCulture,
                $"{recipe.Name ?? "recipe"} · seed {seed} · {width}×{height} · {chosen.Run.Library} {chosen.Run.Version} · one exposure ({ev:+0.0;-0.0} EV)");
            SheetLayout layout = ContactSheet.Layout(sizes, labels, 3, title);
            PngEncoder.Write(Path.Combine(outDir, "compare.png"), ContactSheet.Compose(layout, tiles), layout.Width, layout.Height, PngColor.Rgb);
            File.WriteAllText(Path.Combine(outDir, "compare.json"), VistaJson.Serialize(new DenoiseCompareReport(
                renderer.DeviceName, recipe, seed, camera, width, height, spp, referenceSpp, ground, exposure, rawError, rows, failures, loupe, loupeCheck,
                timings)));
            log.WriteLine($"images and report: {outDir}");
        }
        return 0;
    }

    private static RenderOutput Render(D3D12VistaRenderer renderer, VistaScene scene, RenderSettings settings, HashSet<Aov> aovs) =>
        renderer.Render(scene, settings, aovs, new BatchPlanner(settings.Rendered.Width, settings.Rendered.Height, settings.SamplesPerPixel, settings.Batching));

    /// <summary>
    /// Denoises <paramref name="loupe"/> of the input alone, with the default (frame-metered) scale and with the automatic one, and
    /// compares both with the same window of <paramref name="frame"/>, the denoised frame.
    /// </summary>
    private static LoupeCheck Loupe(OidnDenoiser denoiser, RenderOutput raw, float[] rawLinear, ExposureInfo exposure, DenoiseSettings settings,
        PixelWindow loupe, float[] frame, double ev)
    {
        var window = new RenderOutput(loupe.Width, loupe.Height, loupe.Crop(raw.Xyz, raw.Width, 3),
            new Dictionary<Aov, float[]>
            {
                [Aov.Albedo] = loupe.Crop(raw.Aovs[Aov.Albedo], raw.Width, 3),
                [Aov.Normal] = loupe.Crop(raw.Aovs[Aov.Normal], raw.Width, 3),
            }, raw.Stats);
        float[] linear = loupe.Crop(rawLinear, raw.Width, 3);
        float[] crop = loupe.Crop(frame, raw.Width, 3);
        byte[] cropRgb = DisplayTransform.ToSrgb8(crop, ev, DisplayTransform.DefaultCurve);
        (double Rmse, double Max) Against(DenoiseSettings way)
        {
            DenoiseResult result = denoiser.Denoise(Denoising.Prepare(window, linear, Denoising.InputScale(way, exposure), out _), way);
            if (!result.Succeeded)
                throw new InvalidOperationException($"The loupe did not denoise: {result.Problem}");
            return (ImageError.DisplayRmse(DisplayTransform.ToSrgb8(result.Color, ev, DisplayTransform.DefaultCurve), cropRgb), ImageError.MaxRelativeDifference(result.Color, crop));
        }
        (double rmse, double max) = Against(settings);
        (double autoRmse, _) = Against(settings with { InputScale = DenoiseInputScale.Auto });
        return new LoupeCheck(rmse, max, autoRmse);
    }

    /// <summary>Renders the view at <paramref name="timingWidth"/> and times the default settings on each device.</summary>
    private static List<DenoiseTiming> Timings(D3D12VistaRenderer renderer, OidnDenoiser denoiser, VistaScene scene, RenderSettings settings,
        int timingWidth, IReadOnlyList<DenoiseDevice> devices, DenoiseSettings defaults, TextWriter log)
    {
        int timingHeight = Math.Max(1, (int)Math.Round(timingWidth * 9.0 / 16.0));
        var big = settings with { Width = timingWidth, Height = timingHeight };
        RenderOutput raw = Render(renderer, scene, big, [Aov.Albedo, Aov.Normal]);
        ExposureInfo exposure = AutoExposure.Measure(Render(renderer, scene, AutoExposure.MeterSettings(big), []).Xyz, scene.Camera.ExposureEv);
        float[] linear = DisplayTransform.XyzToLinearRec709(raw.Xyz);
        var timings = new List<DenoiseTiming>();
        foreach (DenoiseDevice device in devices)
        {
            DenoiseSettings way = defaults with { Device = device };
            DenoiseInput input = Denoising.Prepare(raw, linear, Denoising.InputScale(way, exposure), out _);
            var runs = new List<DenoiseResult>();
            for (int k = 0; k < TimingRuns; k++)
                runs.Add(denoiser.Denoise(input, way));
            if (runs.Any(r => !r.Succeeded))
            {
                log.WriteLine($"  {timingWidth}×{timingHeight} on {device}: not denoised: {runs.First(r => !r.Succeeded).Problem}");
                continue;
            }
            double commit = runs[0].Run!.CommitSeconds;
            double[] execute = [.. runs.Skip(1).Select(r => r.Run!.ExecuteSeconds).Order()];
            bool identical = runs.Skip(1).All(r => ImageError.BitIdentical(r.Color, runs[0].Color));
            var timing = new DenoiseTiming(device, runs[0].Run!.Device, timingWidth, timingHeight, commit * 1e3, execute[execute.Length / 2] * 1e3,
                runs[0].Run!.ExecuteSeconds * 1e3, identical, raw.Stats.TraceSeconds);
            timings.Add(timing);
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {timingWidth}×{timingHeight} on {timing.DeviceName}: prepare {timing.CommitMs:0} ms, first filter {timing.FirstExecuteMs:0} ms, " +
                $"then {timing.ExecuteMs:0} ms (median of {execute.Length}); {(identical ? "bit-identical" : "NOT bit-identical")} every run; " +
                $"the render itself took {timing.RenderSeconds:0.00} s GPU"));
        }
        return timings;
    }

    private static byte[] Crop8(byte[] rgb, int width, PixelWindow window)
    {
        var crop = new byte[window.Pixels * 3];
        for (int row = 0; row < window.Height; row++)
            Array.Copy(rgb, ((window.Y + row) * width + window.X) * 3, crop, row * window.Width * 3, window.Width * 3);
        return crop;
    }

    private static string Name(DenoiseSettings way) => string.Create(CultureInfo.InvariantCulture,
        $"{way.Device.ToString().ToLowerInvariant(),-6}  {way.Quality.ToString().ToLowerInvariant(),-8}  {way.Auxiliary.ToString().ToLowerInvariant(),-11}  {way.InputScale.ToString().ToLowerInvariant(),-8}");

    private static string Describe(DenoiseSettings way) =>
        $"{way.Quality.ToString().ToLowerInvariant()}, {way.Auxiliary.ToString().ToLowerInvariant()} aux, {way.InputScale.ToString().ToLowerInvariant()} scale";

    private static string FileName(DenoiseSettings way) =>
        string.Join('-', way.Device, way.Quality, way.Auxiliary, way.InputScale).ToLowerInvariant();

    private static int Int(Dictionary<string, string> values, string key, int fallback, List<string> problems)
    {
        if (!values.TryGetValue(key, out string? text))
            return fallback;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0)
            return value;
        problems.Add($"{key} must be a positive integer, got '{text}'.");
        return fallback;
    }
}

/// <summary>One way of denoising in <c>--denoise-compare</c>.</summary>
/// <param name="Settings">The way.</param>
/// <param name="RelativeMse">relMSE against the noise-free image (the reference's noise taken out), at the meter's exposure.</param>
/// <param name="EqualErrorSpp">The samples per pixel at which the raw render has that error; null when it is below what the reference resolves.</param>
/// <param name="DisplayRmse">RMS difference from the reference's PNG, 8-bit steps.</param>
/// <param name="MeanBias">Mean luminance over the reference's, minus 1.</param>
/// <param name="BitIdentical">Whether denoising the same input twice gave the same bits.</param>
/// <param name="CommitMs">Preparing the filter, ms.</param>
/// <param name="ExecuteMs">Filtering with the prepared filter (upload and download included), ms.</param>
/// <param name="MaxRelativeDifferenceFromCpu">For a GPU, the largest relative difference from the CPU's image with the same settings.</param>
/// <param name="ClampedValues">Colour values given to the filter as 0.</param>
/// <param name="Run">What the denoiser reported.</param>
internal sealed record DenoiseCompareRow(DenoiseSettings Settings, double RelativeMse, double? EqualErrorSpp, double DisplayRmse,
    double MeanBias, bool BitIdentical, double CommitMs, double ExecuteMs, double? MaxRelativeDifferenceFromCpu, long ClampedValues, DenoiserRun Run);

/// <summary>A way of denoising that did not run, and why.</summary>
/// <param name="Settings">The way.</param>
/// <param name="Problem">Why.</param>
internal sealed record DenoiseFailure(DenoiseSettings Settings, string Problem);

/// <summary>A loupe of the input denoised alone, against the same window of the denoised frame.</summary>
/// <param name="DisplayRmse">With the frame-metered input scale: RMS difference of the PNGs, 8-bit steps.</param>
/// <param name="MaxRelativeDifference">The same, the largest relative difference in linear values.</param>
/// <param name="AutoDisplayRmse">With the filter's automatic scale (from the loupe's own pixels).</param>
internal sealed record LoupeCheck(double DisplayRmse, double MaxRelativeDifference, double AutoDisplayRmse);

/// <summary>The default settings timed at a final's size on one device.</summary>
/// <param name="Device">The device kind.</param>
/// <param name="DeviceName">Its name.</param>
/// <param name="Width">Image width.</param>
/// <param name="Height">Image height.</param>
/// <param name="CommitMs">Preparing the filter, ms.</param>
/// <param name="ExecuteMs">Filtering with the prepared filter, the median of the later runs, ms.</param>
/// <param name="FirstExecuteMs">The first run's filtering, ms (a GPU's first run includes its warm-up).</param>
/// <param name="BitIdentical">Whether every run gave the first run's bits.</param>
/// <param name="RenderSeconds">GPU time of the render that was denoised, s.</param>
internal sealed record DenoiseTiming(DenoiseDevice Device, string DeviceName, int Width, int Height, double CommitMs, double ExecuteMs, double FirstExecuteMs,
    bool BitIdentical, double RenderSeconds);

/// <summary>What <c>--denoise-compare --out</c> writes.</summary>
/// <param name="Device">The GPU that rendered.</param>
/// <param name="Recipe">The recipe.</param>
/// <param name="Seed">The seed.</param>
/// <param name="Camera">The camera.</param>
/// <param name="Width">Image width.</param>
/// <param name="Height">Image height.</param>
/// <param name="SamplesPerPixel">Samples per pixel of the denoised render.</param>
/// <param name="ReferenceSamplesPerPixel">Samples per pixel of the reference, both halves.</param>
/// <param name="GroundAltitudeMetres">The scene's base altitude.</param>
/// <param name="Exposure">The meter's exposure, at which the errors are measured.</param>
/// <param name="Raw">The raw render's error.</param>
/// <param name="Ways">Every way of denoising that ran.</param>
/// <param name="Failures">Every way that did not, and why.</param>
/// <param name="Loupe">The loupe window.</param>
/// <param name="LoupeCheck">The loupe denoised alone against the denoised frame.</param>
/// <param name="Timings">The default settings at the timing width, per device.</param>
internal sealed record DenoiseCompareReport(
    string Device,
    Recipe Recipe,
    ulong Seed,
    CameraSpec Camera,
    int Width,
    int Height,
    int SamplesPerPixel,
    int ReferenceSamplesPerPixel,
    double GroundAltitudeMetres,
    ExposureInfo Exposure,
    RelativeError Raw,
    IReadOnlyList<DenoiseCompareRow> Ways,
    IReadOnlyList<DenoiseFailure> Failures,
    PixelWindow Loupe,
    LoupeCheck LoupeCheck,
    IReadOnlyList<DenoiseTiming> Timings);
