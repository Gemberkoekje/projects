using System.Globalization;
using Vista.Atmosphere;
using Vista.Cli;
using Vista.Output;
using Vista.Rendering;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --pools-compare</c> (vista-plan §10 M1.13b verify): one view with the valleys' pools rendered as the preview renders
/// it, through the LUTs and the march, through the reference atmosphere (the pools path-traced as part of the medium) and through the
/// reference with the terrain's shadow on every event in the pools' reach; then the light of the march's render against each
/// reference's: over the frame, by thirds top to bottom, and by distance (<see cref="RenderComparison"/>). It takes the preview's own
/// options, but for <c>--atmosphere</c>; <c>--reference-spp</c> sets the references' samples (default 8 times the march's).
/// <code>
/// Vista.App --pools-compare --recipe r.json --seed 42 [--camera c.json] --out dir/view.png [--reference-spp n] [--modes luts,reference,referenceShadowed]
///           [any other --preview option]
/// </code>
/// It writes <c>view.luts.*</c>, <c>view.reference.*</c> and <c>view.referenceShadowed.*</c> (each at the incident meter's exposure,
/// which reads the LUTs alone, so one exposure for all), and with the environment variable <see cref="LowAirOverrides.Variable"/> the
/// march's settings can be varied without a build.
/// </summary>
internal static class PoolsCompare
{
    /// <summary>The flag that selects this mode.</summary>
    public const string Flag = "--pools-compare";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --pools-compare --recipe r.json --seed 42 [--camera c.json] --out dir/view.png [--reference-spp n]\n" +
        "                 [--modes luts,reference,referenceShadowed] [other --preview options]  (the pools' march against the reference)";

    /// <summary>Runs the comparison; returns 0, or 1 for bad arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        var rest = new List<string> { "--preview" };
        int? referenceSpp = null;
        string[] modes = ["luts", "reference", "referenceShadowed"];
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] == "--reference-spp" && i + 1 < args.Count && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out int spp) && spp > 0)
            {
                referenceSpp = spp;
                i++;
            }
            else if (args[i] == "--modes" && i + 1 < args.Count)
            {
                modes = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else if (args[i] == "--atmosphere")
            {
                Console.Error.WriteLine("--pools-compare chooses the atmosphere itself: leave out --atmosphere.");
                Console.Error.WriteLine(Usage);
                return 1;
            }
            else
            {
                rest.Add(args[i]);
            }
        }
        if (!PreviewCommandLine.TryParse(rest, out PreviewRequest? parsed, out IReadOnlyList<string> errors))
        {
            foreach (string error in errors)
                Console.Error.WriteLine(error);
            Console.Error.WriteLine(Usage);
            return 1;
        }
        PreviewRequest request = parsed! with
        {
            Debug = new HashSet<DebugOutput>(parsed!.Debug) { DebugOutput.Depth, DebugOutput.Variance },
            LowAir = LowAirOverrides.Apply(LowAirSettings.Default),
        };
        string directory = Path.GetDirectoryName(Path.GetFullPath(request.OutPath))!;
        string name = Path.GetFileNameWithoutExtension(request.OutPath);

        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}");
        var session = new PreviewSession(renderer, eroder: renderer, erosionCache: ErosionCache.Default);
        var renders = new Dictionary<string, RenderOutput>(StringComparer.Ordinal);
        foreach (string mode in modes)
        {
            if (!Enum.TryParse(mode, ignoreCase: true, out AtmosphereMode atmosphere) || atmosphere == AtmosphereMode.None)
            {
                Console.Error.WriteLine($"Unknown mode '{mode}'.");
                return 1;
            }
            int spp = atmosphere == AtmosphereMode.Luts ? request.SamplesPerPixel : referenceSpp ?? request.SamplesPerPixel * 8;
            // A reference's raw image is kept beside it and read back while its request is the same (the march's settings aside).
            string raw = Path.Combine(directory, $"{name}.{mode}.raw");
            string key = string.Join(' ', rest.Where(a => a != "--out")) + $" {mode} {spp}";
            if (atmosphere != AtmosphereMode.Luts && ReadRaw(raw, key) is { } kept)
            {
                renders[mode] = kept;
                log.WriteLine($"{name}.{mode}: read back from {Path.GetFileName(raw)}");
                continue;
            }
            session.Run(request with { Atmosphere = atmosphere, SamplesPerPixel = spp, OutPath = Path.Combine(directory, $"{name}.{mode}.png") });
            RenderOutput render = session.LastRender!;
            renders[mode] = render;
            if (atmosphere != AtmosphereMode.Luts)
                WriteRaw(raw, key, render);
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{name}.{mode}: {spp} spp, {render.Stats.TraceSeconds:0.000} s GPU, {render.Stats.RaysPerSample():0.00} rays a sample"));
        }

        if (!renders.TryGetValue("luts", out RenderOutput? luts))
            return 0;
        foreach ((string mode, RenderOutput reference) in renders)
        {
            if (mode == "luts")
                continue;
            log.WriteLine($"{name}: the LUTs and the march against {mode}:");
            (double overall, double[] thirds) = Thirds(luts, reference);
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  the frame {overall:+0.0%;-0.0%}; by thirds, top to bottom, {thirds[0]:+0.0%;-0.0%}, {thirds[1]:+0.0%;-0.0%}, {thirds[2]:+0.0%;-0.0%}"));
            foreach (ComparisonBin bin in RenderComparison.Compare(luts.Aovs[Aov.Depth], luts.Xyz, luts.Aovs[Aov.Variance], reference.Xyz, reference.Aovs[Aov.Variance]))
            {
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {bin.Bin,-9} {bin.Pixels,6} px  {bin.Error,7:+0.0%;-0.0%} ± {bin.ErrorNoise:0.0%}"));
            }
        }
        return 0;
    }

    private static void WriteRaw(string path, string key, RenderOutput render)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(key);
        writer.Write(render.Width);
        writer.Write(render.Height);
        foreach (float[] data in new[] { render.Xyz, render.Aovs[Aov.Depth], render.Aovs[Aov.Variance] })
        {
            writer.Write(data.Length);
            foreach (float v in data)
                writer.Write(v);
        }
    }

    private static RenderOutput? ReadRaw(string path, string key)
    {
        if (!File.Exists(path))
            return null;
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadString() != key)
            return null;
        int width = reader.ReadInt32(), height = reader.ReadInt32();
        float[] Next()
        {
            var data = new float[reader.ReadInt32()];
            for (int i = 0; i < data.Length; i++)
                data[i] = reader.ReadSingle();
            return data;
        }
        float[] xyz = Next(), depth = Next(), variance = Next();
        return new RenderOutput(width, height, xyz, new Dictionary<Aov, float[]> { [Aov.Depth] = depth, [Aov.Variance] = variance }, new RenderStats());
    }

    /// <summary>The candidate's mean luminance against the reference's, over the frame and by thirds of its rows, top to bottom (candidate ÷ reference − 1).</summary>
    private static (double Overall, double[] Thirds) Thirds(RenderOutput candidate, RenderOutput reference)
    {
        var c = new double[3];
        var r = new double[3];
        int width = candidate.Width, height = candidate.Height;
        for (int y = 0; y < height; y++)
        {
            int third = Math.Min(3 * y / height, 2);
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                c[third] += candidate.Xyz[p * 3 + 1];
                r[third] += reference.Xyz[p * 3 + 1];
            }
        }
        double overall = r.Sum() > 0.0 ? c.Sum() / r.Sum() - 1.0 : 0.0;
        return (overall, [.. Enumerable.Range(0, 3).Select(k => r[k] > 0.0 ? c[k] / r[k] - 1.0 : 0.0)]);
    }
}

/// <summary>
/// The march's settings from the environment (<see cref="Variable"/>: <c>Name=value</c> pairs separated by <c>;</c>, the names of
/// <see cref="LowAirSettings"/>' properties), for measurements: varying the steps or the octaves against the reference without a build.
/// </summary>
internal static class LowAirOverrides
{
    /// <summary>The environment variable.</summary>
    public const string Variable = "VISTA_LOW_AIR";

    /// <summary><paramref name="settings"/> with the environment's overrides, or as they are without any.</summary>
    public static LowAirSettings Apply(LowAirSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? text = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(text))
            return settings;
        LowAirSettings result = settings with { };
        foreach (string pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            var property = typeof(LowAirSettings).GetProperty(parts[0]) ?? throw new ArgumentException($"{Variable}: no setting '{parts[0]}'.");
            object value = property.PropertyType == typeof(int)
                ? (object)int.Parse(parts[1], CultureInfo.InvariantCulture)
                : double.Parse(parts[1], CultureInfo.InvariantCulture);
            property.SetValue(result, value);
        }
        return result;
    }
}
