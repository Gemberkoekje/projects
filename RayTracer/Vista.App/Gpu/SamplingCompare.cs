using System.Globalization;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --sampling-compare</c> (vista-plan §10 M1.5 verify): one view rendered with M1.4's estimator (the sun alone
/// sampled, Russian roulette from bounce 3: M1.4's images bit for bit) and with M1.5's (sun and sky sampled, MIS against the
/// bounce, roulette from bounce 2), each as <c>--replicates</c> independent renders of
/// <c>--spp</c> samples on their own random streams, compared pixel by pixel (<see cref="EstimatorComparison"/>): do they
/// agree within each pixel's standard error, and how much less noise does the new one leave on the terrain at equal samples,
/// at what cost per sample. <c>--rr-starts</c> also renders the new estimator with Russian roulette starting at each of the
/// given bounces, for the re-measurement of where it should start. <c>--baseline mis</c> compares the new estimator with itself
/// on other streams: the null test, which shows what the statistics look like when nothing differs.
/// <code>
/// Vista.App --sampling-compare --recipe r.json [--camera c.json] [--seed 42] [--width 320] [--spp 128] [--replicates 32]
///                              [--bounces 6] [--rr-starts 0,1,2,3,4,7] [--baseline sunOnly | mis] [--out dir]
/// </code>
/// With <c>--out</c> it writes each estimator's mean image and one render of each at one exposure (the new estimator's), a
/// sheet of them, and the numbers.
/// </summary>
internal static class SamplingCompare
{
    /// <summary>The flag that selects this mode.</summary>
    public const string Flag = "--sampling-compare";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --sampling-compare --recipe r.json [--camera c.json] [--seed 42] [--width 320] [--spp 128] [--replicates 32]\n" +
        "                 [--bounces 6] [--rr-starts 0,1,2,3,4,7] [--baseline sunOnly | mis] [--out dir]\n" +
        "                 (M1.4's estimator against MIS, or MIS against itself: agreement, noise, cost)";

    /// <summary>Random streams of the baseline's replicates start here, the candidate's at 1: every render independent.</summary>
    private const uint BaselineStreams = 1_000_000;

    /// <summary>M1.4's Russian-roulette start, for its estimator.</summary>
    private const int M14RussianRouletteStart = 3;

    /// <summary>Runs the comparison; returns 0, or 1 for bad arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        var problems = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Count; i++)
        {
            if (i + 1 < args.Count && args[i] is "--recipe" or "--camera" or "--seed" or "--width" or "--spp" or "--replicates" or "--bounces" or "--rr-starts" or "--baseline" or "--out")
                values[args[i]] = args[++i];
            else
                problems.Add($"Unknown or valueless argument '{args[i]}'.");
        }
        if (!values.ContainsKey("--recipe"))
            problems.Add("--recipe is required.");
        ulong seed = values.TryGetValue("--seed", out string? s) && ulong.TryParse(s, CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 42;
        int width = Int(values, "--width", 320, problems);
        int spp = Int(values, "--spp", 128, problems);
        int replicates = Int(values, "--replicates", 32, problems);
        int bounces = values.ContainsKey("--bounces") ? Int(values, "--bounces", 6, problems) : new RenderSettings { Width = 1, Height = 1 }.MaxBounces;
        if (replicates < 2)
            problems.Add("--replicates must be at least 2.");
        LightSampling baselineSampling = LightSampling.SunOnly;
        if (values.TryGetValue("--baseline", out string? baselineText)
            && (!Enum.TryParse(baselineText, ignoreCase: true, out baselineSampling) || !Enum.IsDefined(baselineSampling) || baselineSampling == LightSampling.None))
            problems.Add($"--baseline must be sunOnly or mis, got '{baselineText}'.");
        var starts = new List<int>();
        if (values.TryGetValue("--rr-starts", out string? startsText))
        {
            foreach (string part in startsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int start) && start <= 64)
                    starts.Add(start);
                else
                    problems.Add($"--rr-starts takes bounces from 0 to 64, got '{part}'.");
            }
        }
        if (problems.Count > 0)
        {
            foreach (string problem in problems)
                Console.Error.WriteLine(problem);
            Console.Error.WriteLine(Usage);
            return 1;
        }

        Recipe recipe = VistaJson.ParseRecipe(File.ReadAllText(values["--recipe"]));
        HeightField terrain = TerrainGenerator.Generate(recipe, seed);
        TerrainMesh mesh = HeightFieldMesher.Build(terrain);
        CameraSpec camera = values.TryGetValue("--camera", out string? cameraPath) ? VistaJson.ParseCamera(File.ReadAllText(cameraPath)) : DefaultCamera.For(terrain);
        Almanac almanac = Almanac.For(recipe);
        (double ground, string groundFrom) = Places.BaseAltitude(recipe, almanac.Place);
        var scene = new VistaScene(recipe, seed, camera, terrain, mesh, almanac.Sun, MaterialCatalog.M0, AtmosphereModel.Default, ground);
        int height = Math.Max(1, (int)Math.Round(width * 9.0 / 16.0));

        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}");
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{recipe.Name ?? "recipe"}, seed {seed}, {width}×{height}, {replicates} renders × {spp} spp each, {bounces} bounces; sun {almanac.Sun.ElevationDegrees:0.0}° up at " +
            $"{almanac.Sun.AzimuthDegrees:0}°; base {ground:0} m ({groundFrom})"));
        var settings = new RenderSettings { Width = width, Height = height, SamplesPerPixel = spp, MaxBounces = bounces };
        var baseline = new Replicated(baselineSampling == LightSampling.SunOnly
            ? settings with { LightSampling = LightSampling.SunOnly, RussianRouletteStart = M14RussianRouletteStart }
            : settings, BaselineStreams);
        var candidate = new Replicated(settings, 0);
        var sweep = new List<(int Start, Replicated Estimator)>();
        foreach (int start in starts)
            sweep.Add((start, start == settings.RussianRouletteStart ? candidate : new Replicated(settings with { RussianRouletteStart = start }, 0)));
        RenderInterleaved(renderer, scene, [baseline, candidate, .. sweep.Select(e => e.Estimator).Where(e => e != candidate)], replicates);
        foreach ((string label, Replicated e) in new[]
        {
            (baselineSampling == LightSampling.SunOnly ? "M1.4 (sun only, roulette from bounce 3)" : "mis on other streams (the null test)", baseline),
            ("mis", candidate),
        })
        {
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {label}: {e.Seconds:0.00} s GPU, {e.NsPerSample:0.00} ns per sample, {e.RaysPerSample:0.00} rays per sample"));
        }

        // Terrain pixels away from the skyline, by sample 0's first hit in the first render (the camera ray and its jitter are
        // the estimators' common part).
        bool[] onTerrain = EstimatorComparison.InteriorTerrain(candidate.First!.Aovs[Aov.Depth], width, height);
        EstimatorAgreement agreement = EstimatorComparison.Compare(candidate.Stats, baseline.Stats, onTerrain);
        double timeRatio = candidate.NsPerSample / baseline.NsPerSample;
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  agreement over {agreement.Pixels} pixels, {replicates * spp} spp each: mean z {agreement.MeanZ:+0.000;-0.000;0.000} (± {1.0 / Math.Sqrt(Math.Max(agreement.Pixels, 1)):0.000}), " +
            $"RMS z {agreement.RmsZ:0.000}, |z| > 3 in {agreement.FractionBeyond3:0.00%} (normal: 0.27%), |z| > 4 in {agreement.FractionBeyond4:0.000%} (0.006%), " +
            $"max |z| {agreement.MaxAbsZ:0.0}"));
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  image mean luminance: mis ÷ baseline − 1 = {agreement.MeanLuminanceError:+0.000%;-0.000%;0.000%} ± {agreement.MeanLuminanceNoise:0.000%}"));
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  terrain away from the sky ({agreement.TerrainPixels} pixels) at {spp} spp: variance {agreement.VarianceReduction:0.00}× lower, relative variance " +
            $"{agreement.RelativeVarianceReduction:0.00}× (median pixel {agreement.MedianPixelVarianceReduction:0.00}×); time per sample {timeRatio:0.00}×; " +
            $"efficiency {agreement.VarianceReduction / timeRatio:0.00}× (relative: {agreement.RelativeVarianceReduction / timeRatio:0.00}×)"));

        var rows = new List<RussianRouletteRow>();
        if (sweep.Count > 0)
        {
            log.WriteLine("  Russian roulette (mis): start  ns/sample  rays/sample  variance  relative variance  efficiency  (all against the default start)");
            foreach ((int start, Replicated m) in sweep)
            {
                EstimatorAgreement against = EstimatorComparison.Compare(m.Stats, candidate.Stats, onTerrain);
                double variance = 1.0 / against.VarianceReduction;
                double relative = 1.0 / against.RelativeVarianceReduction;
                double efficiency = against.VarianceReduction / (m.NsPerSample / candidate.NsPerSample);
                rows.Add(new RussianRouletteRow(start, m.NsPerSample, m.RaysPerSample, variance, relative, efficiency));
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"    {start,5}  {m.NsPerSample,9:0.00}  {m.RaysPerSample,11:0.00}  {variance,8:0.000}  {relative,17:0.000}  {efficiency,10:0.000}"));
            }
        }

        if (values.TryGetValue("--out", out string? outDir))
        {
            Directory.CreateDirectory(outDir);
            double ev = AutoExposure.Measure(candidate.MeanXyz, camera.ExposureEv).AppliedEv;
            var tiles = new List<byte[]>();
            (string baseName, string baseLabel) = baselineSampling == LightSampling.SunOnly ? ("m14", "M1.4: sun only") : ("mis-other", "MIS, other streams");
            (string Name, float[] Xyz, string Label)[] images =
            [
                (baseName, baseline.First!.Xyz, $"{baseLabel}, one render of {spp} spp"),
                ("mis", candidate.First.Xyz, $"M1.5: sun + sky, MIS, one render of {spp} spp"),
                (baseName + "-mean", baseline.MeanXyz, $"{baseLabel}, {replicates * spp} spp"),
                ("mis-mean", candidate.MeanXyz, $"M1.5: sun + sky, MIS, {replicates * spp} spp"),
            ];
            foreach ((string name, float[] xyz, _) in images)
            {
                byte[] rgb = DisplayTransform.ToSrgb8(DisplayTransform.XyzToLinearRec709(xyz), ev);
                PngEncoder.Write(Path.Combine(outDir, name + ".png"), rgb, width, height, PngColor.Rgb);
                tiles.Add(rgb);
            }
            SheetLayout layout = ContactSheet.Layout([.. images.Select(_ => (width, height))], [.. images.Select(i => i.Label)], 2,
                string.Create(CultureInfo.InvariantCulture, $"{recipe.Name ?? "recipe"} · seed {seed} · one exposure ({ev:+0.0;-0.0} EV)"));
            PngEncoder.Write(Path.Combine(outDir, "compare.png"), ContactSheet.Compose(layout, tiles), layout.Width, layout.Height, PngColor.Rgb);
            File.WriteAllText(Path.Combine(outDir, "compare.json"), VistaJson.Serialize(new SamplingCompareReport(
                renderer.DeviceName, recipe, seed, camera, width, height, spp, replicates, ground,
                baseline.NsPerSample, baseline.RaysPerSample, candidate.NsPerSample, candidate.RaysPerSample, agreement, rows)));
            log.WriteLine($"images and report: {outDir}");
        }
        return 0;
    }

    /// <summary>
    /// Renders replicate k of every estimator before replicate k + 1 of any, so a drift in the GPU's speed (its clocks, another
    /// program on it) falls on all of them alike and cancels in their ratios of time.
    /// </summary>
    private static void RenderInterleaved(D3D12VistaRenderer renderer, VistaScene scene, IReadOnlyList<Replicated> estimators, int replicates)
    {
        for (int k = 0; k < replicates; k++)
        {
            foreach (Replicated e in estimators)
            {
                RenderSettings replicate = e.Settings with { RandomStream = e.FirstStream + (uint)k + 1u };
                var planner = new BatchPlanner(replicate.Width, replicate.Height, replicate.SamplesPerPixel, replicate.Batching);
                e.Add(renderer.Render(scene, replicate, k == 0 ? new HashSet<Aov> { Aov.Depth } : new HashSet<Aov>(), planner));
            }
        }
    }

    /// <summary>One estimator's replicates: their statistics, the first render, the mean image, and the cost per sample.</summary>
    private sealed class Replicated(RenderSettings settings, uint firstStream)
    {
        private readonly double[] _sum = new double[settings.Width * settings.Height * 3];

        public RenderSettings Settings { get; } = settings;

        /// <summary>Replicate k draws random stream FirstStream + k + 1.</summary>
        public uint FirstStream { get; } = firstStream;

        public ReplicateStats Stats { get; } = new(settings.Width * settings.Height);

        public RenderOutput? First { get; private set; }

        public double Seconds { get; private set; }

        public double NsPerSample => Seconds * 1e9 / _samples;

        public double RaysPerSample => _rays / _samples;

        public float[] MeanXyz => [.. _sum.Select(v => (float)(v / Stats.Count))];

        private double _rays;
        private long _samples;

        public void Add(RenderOutput output)
        {
            First ??= output;
            Stats.Add(output.Xyz);
            for (int i = 0; i < _sum.Length; i++)
                _sum[i] += output.Xyz[i];
            Seconds += output.Stats.TraceSeconds;
            _rays += output.Stats.RaysTraced;
            _samples += output.Stats.PixelSamples;
        }
    }

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

/// <summary>One Russian-roulette start in <c>--sampling-compare --rr-starts</c>, against the default start.</summary>
/// <param name="Start">The bounce from which roulette may end a path.</param>
/// <param name="NsPerSample">GPU time per pixel-sample.</param>
/// <param name="RaysPerSample">Rays per pixel-sample.</param>
/// <param name="TerrainVariance">Σ terrain-pixel variance at equal samples, relative to the default start's.</param>
/// <param name="TerrainRelativeVariance">The same for each pixel's variance over its mean squared.</param>
/// <param name="Efficiency">1 / (variance × time), relative to the default start's.</param>
internal sealed record RussianRouletteRow(int Start, double NsPerSample, double RaysPerSample, double TerrainVariance, double TerrainRelativeVariance,
    double Efficiency);

/// <summary>What <c>--sampling-compare --out</c> writes.</summary>
/// <param name="Device">The GPU.</param>
/// <param name="Recipe">The recipe.</param>
/// <param name="Seed">The seed.</param>
/// <param name="Camera">The camera.</param>
/// <param name="Width">Image width.</param>
/// <param name="Height">Image height.</param>
/// <param name="SamplesPerPixel">Samples per pixel of each render.</param>
/// <param name="Replicates">Independent renders per estimator.</param>
/// <param name="GroundAltitudeMetres">The scene's base altitude.</param>
/// <param name="SunOnlyNsPerSample">M1.4's estimator: GPU time per pixel-sample.</param>
/// <param name="SunOnlyRaysPerSample">M1.4's estimator: rays per pixel-sample.</param>
/// <param name="MisNsPerSample">MIS: GPU time per pixel-sample.</param>
/// <param name="MisRaysPerSample">MIS: rays per pixel-sample.</param>
/// <param name="Agreement">The per-pixel comparison.</param>
/// <param name="RussianRoulette">The roulette sweep, if one was asked for.</param>
internal sealed record SamplingCompareReport(
    string Device,
    Recipe Recipe,
    ulong Seed,
    CameraSpec Camera,
    int Width,
    int Height,
    int SamplesPerPixel,
    int Replicates,
    double GroundAltitudeMetres,
    double SunOnlyNsPerSample,
    double SunOnlyRaysPerSample,
    double MisNsPerSample,
    double MisRaysPerSample,
    EstimatorAgreement Agreement,
    IReadOnlyList<RussianRouletteRow> RussianRoulette);
