using System.Globalization;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.Sliders;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --diffuse-compare</c> (vista-plan §10 M1.12 verify): views of one world rendered with the ground's materials as
/// Lambert's surfaces and as rough ones (Oren–Nayar in Fujii's form), at one exposure each (the Lambert render's, by the average
/// meter), and how much the rough surfaces brighten or darken the ground: the mean luminance over the pixels where sample 0 hit the
/// terrain, rough over Lambert, and the share of those pixels a tenth brighter or darker. A low sun behind the camera is where rough
/// ground backscatters: the golden-hour look.
/// <para><c>Vista.App --canopy-compare</c> (vista-plan §10 M1.12b verify) does the same for the forest: its canopy shaded as rough
/// diffuse ground at its own roughness against its kernels (crowns' shadows and the hot spot), over the pixels whose material is
/// forest. <c>--altitude</c> pins the scene's base altitude, to bring forest into a view, and <c>--sun-elevation</c> the sun's height.</para>
/// <code>
/// Vista.App --diffuse-compare --recipe r.json --cameras a.camera.json,b.camera.json [--seed 42] [--width 640] [--spp 128] [--altitude m] [--out dir]
/// Vista.App --canopy-compare  --recipe r.json --cameras a.camera.json,b.camera.json [--seed 42] [--width 640] [--spp 128] [--altitude m] [--out dir]
/// </code>
/// With <c>--out</c> it writes per view the two renders and a map of the ratio (blue darker, red brighter, white the same), and a
/// sheet of them, three cells to a view.
/// </summary>
internal static class DiffuseCompare
{
    /// <summary>The flag that selects the diffuse comparison.</summary>
    public const string Flag = "--diffuse-compare";

    /// <summary>The flag that selects the canopy comparison (M1.12b).</summary>
    public const string CanopyFlag = "--canopy-compare";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --diffuse-compare --recipe r.json --cameras a.camera.json[,b.camera.json] [--seed 42] [--width 640] [--spp 128] [--altitude m]\n" +
        "                 [--sun-elevation deg] [--out dir]\n" +
        "                 (the ground as Lambert's surfaces against rough ones: the luminance ratio on the terrain)\n" +
        "       Vista.App --canopy-compare --recipe r.json --cameras a.camera.json[,b.camera.json] [--seed 42] [--width 640] [--spp 128] [--altitude m]\n" +
        "                 [--sun-elevation deg] [--out dir]\n" +
        "                 (the forest as rough diffuse ground against its canopy's kernels: the luminance ratio on the forest)";

    /// <summary>Runs the comparison its first argument names; returns 0, or 1 for bad arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        bool canopy = args.Count > 0 && args[0] == CanopyFlag;
        var problems = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Count; i++)
        {
            if (i + 1 < args.Count && args[i] is "--recipe" or "--cameras" or "--seed" or "--width" or "--spp" or "--out" or "--altitude" or "--sun-elevation")
                values[args[i]] = args[++i];
            else
                problems.Add($"Unknown or valueless argument '{args[i]}'.");
        }
        if (!values.ContainsKey("--recipe"))
            problems.Add("--recipe is required.");
        if (!values.ContainsKey("--cameras"))
            problems.Add("--cameras is required.");
        ulong seed = values.TryGetValue("--seed", out string? s) && ulong.TryParse(s, CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 42;
        int width = Int(values, "--width", 640, problems);
        int spp = Int(values, "--spp", 128, problems);
        double? altitude = null;
        if (values.TryGetValue("--altitude", out string? altitudeText))
        {
            if (double.TryParse(altitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double metres) && metres is >= 0.0 and <= SliderPins.MaxAltitudeMetres)
                altitude = metres;
            else
                problems.Add($"--altitude must be metres above sea level, 0 to {SliderPins.MaxAltitudeMetres}, got '{altitudeText}'.");
        }
        double? sunElevation = null;
        if (values.TryGetValue("--sun-elevation", out string? sunText))
        {
            if (double.TryParse(sunText, NumberStyles.Float, CultureInfo.InvariantCulture, out double degrees) && degrees is >= -10.0 and <= 90.0)
                sunElevation = degrees;
            else
                problems.Add($"--sun-elevation must be degrees, -10 to 90, got '{sunText}'.");
        }
        if (problems.Count > 0)
        {
            foreach (string problem in problems)
                Console.Error.WriteLine(problem);
            Console.Error.WriteLine(Usage);
            return 1;
        }

        Recipe recipe = VistaJson.ParseRecipe(File.ReadAllText(values["--recipe"]));
        string[] cameras = values["--cameras"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? outDir = values.GetValueOrDefault("--out");
        if (outDir is not null)
            Directory.CreateDirectory(outDir);
        int height = Math.Max(1, (int)Math.Round(width * 9.0 / 16.0));

        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}");
        var worlds = new WorldCache();
        TerrainWorld world = worlds.World(recipe, seed, WorldGrid.Default, renderer, ErosionCache.Default);
        SliderPins pins = SliderPins.FromRecipe(recipe);
        SliderState sliders = LinkedSliders.Resolve(recipe, pins with { Altitude = altitude ?? pins.Altitude, SunElevation = sunElevation ?? pins.SunElevation }, AtmosphereModel.Default);
        Almanac almanac = sliders.Almanac;
        double ground = sliders.BaseAltitudeMetres;
        AtmosphereModel air = sliders.Air(AtmosphereModel.Default);
        GroundCover cover = GroundCover.For(sliders.Climate, almanac.Place.LatitudeDegrees);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{recipe.Name ?? "recipe"}, seed {seed}, {width}×{height}, {spp} spp, base altitude {ground:0} m; sun {almanac.Sun.ElevationDegrees:0.0}° up at {almanac.Sun.AzimuthDegrees:0}°"));
        (string a, string b) = canopy ? ("rough", "kernels") : ("Lambert", "Oren-Nayar");

        var tiles = new List<(byte[] Rgb, string Label)>();
        foreach (string path in cameras)
        {
            CameraSpec camera = VistaJson.ParseCamera(File.ReadAllText(path));
            TerrainMesh mesh = worlds.Mesh(MeshView.For(camera, width, height, air.PlanetRadiusMetres + ground),
                new MeshSettings { Forest = PreviewSession.ForestOf(cover, ground) });
            var scene = new VistaScene(recipe, seed, camera, world.Surface, mesh, almanac.Sun, MaterialCatalog.Ground, air, ground) { Ground = cover };
            var settings = new RenderSettings { Width = width, Height = height, SamplesPerPixel = spp };
            RenderSettings first = canopy ? settings with { Canopy = CanopyModel.Rough } : settings with { Diffuse = DiffuseModel.Lambert };
            RenderSettings second = canopy ? settings with { Canopy = CanopyModel.Kernels } : settings with { Diffuse = DiffuseModel.OrenNayar };
            RenderOutput before = renderer.Render(scene, first, new HashSet<Aov> { Aov.Depth, Aov.MaterialId }, new BatchPlanner(width, height, spp, settings.Batching));
            RenderOutput after = renderer.Render(scene, second, new HashSet<Aov>(), new BatchPlanner(width, height, spp, settings.Batching));
            float[] depth = before.Aovs[Aov.Depth];
            float[] ids = before.Aovs[Aov.MaterialId];
            // The pixels measured: the terrain's, or (the canopy) the forest's.
            bool Measured(int p) => canopy ? ids[p] == MaterialCatalog.ForestId : depth[p] > 0f && float.IsFinite(depth[p]);
            double sumA = 0.0, sumB = 0.0;
            int count = 0, brighter = 0, darker = 0;
            var ratio = new float[width * height];
            var mask = new bool[width * height];
            for (int p = 0; p < ratio.Length; p++)
            {
                double l = before.Xyz[p * 3 + 1], r = after.Xyz[p * 3 + 1];
                ratio[p] = l > 0.0 ? (float)(r / l) : 1f;
                mask[p] = Measured(p);
                if (!mask[p])
                    continue;
                sumA += l;
                sumB += r;
                count++;
                if (ratio[p] > 1.1f)
                    brighter++;
                else if (ratio[p] < 1f / 1.1f)
                    darker++;
            }
            string name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
            string what = canopy ? "the forest's luminance, kernels over rough" : "the ground's luminance, rough over Lambert";
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {name} (yaw {camera.YawDegrees:0}°): {what}, {sumB / Math.Max(sumA, 1e-30):0.000}; " +
                $"{100.0 * brighter / Math.Max(count, 1):0.0}% of it a tenth brighter, {100.0 * darker / Math.Max(count, 1):0.0}% a tenth darker ({100.0 * count / ratio.Length:0.0}% of the frame)"));
            if (outDir is null)
                continue;
            double ev = AutoExposure.Measure(before.Xyz, camera.ExposureEv).AppliedEv;
            byte[] beforeRgb = DisplayTransform.ToSrgb8(DisplayTransform.XyzToLinearRec709(before.Xyz), ev, DisplayTransform.DefaultCurve, DisplayTransform.DefaultLook);
            byte[] afterRgb = DisplayTransform.ToSrgb8(DisplayTransform.XyzToLinearRec709(after.Xyz), ev, DisplayTransform.DefaultCurve, DisplayTransform.DefaultLook);
            byte[] ratioRgb = RatioMap(ratio, mask);
            string suffixA = canopy ? ".rough.png" : ".lambert.png", suffixB = canopy ? ".kernels.png" : ".orenNayar.png";
            PngEncoder.Write(Path.Combine(outDir, name + suffixA), beforeRgb, width, height, PngColor.Rgb);
            PngEncoder.Write(Path.Combine(outDir, name + suffixB), afterRgb, width, height, PngColor.Rgb);
            PngEncoder.Write(Path.Combine(outDir, name + ".ratio.png"), ratioRgb, width, height, PngColor.Rgb);
            tiles.Add((beforeRgb, $"{name} · {a}"));
            tiles.Add((afterRgb, $"{name} · {b}"));
            tiles.Add((ratioRgb, string.Create(CultureInfo.InvariantCulture,
                $"{name} · {(canopy ? "kernels / rough" : "rough / Lambert")} {sumB / Math.Max(sumA, 1e-30):0.000} on the {(canopy ? "forest" : "ground")}")));
        }
        if (outDir is not null && tiles.Count > 0)
        {
            string title = canopy
                ? "the forest as rough diffuse ground and by its canopy's kernels · one exposure per view · ratio on the forest: blue 0.8 or less, white 1, red 1.25 or more"
                : "Lambert and rough (Oren-Nayar) ground · one exposure per view · ratio: blue 0.8 or less, white 1, red 1.25 or more";
            SheetLayout layout = ContactSheet.Layout([.. tiles.Select(_ => (width, height))], [.. tiles.Select(t => t.Label)], 3, title);
            PngEncoder.Write(Path.Combine(outDir, canopy ? "canopy.png" : "diffuse.png"), ContactSheet.Compose(layout, [.. tiles.Select(t => t.Rgb)]),
                layout.Width, layout.Height, PngColor.Rgb);
        }
        return 0;
    }

    /// <summary>The ratio as colour where <paramref name="mask"/> holds: white at 1, toward blue at 0.8 and below, toward red at 1.25 and above (log-scaled); elsewhere black.</summary>
    private static byte[] RatioMap(float[] ratio, bool[] mask)
    {
        var rgb = new byte[ratio.Length * 3];
        for (int p = 0; p < ratio.Length; p++)
        {
            if (!mask[p])
                continue;
            double t = Math.Clamp(Math.Log(Math.Max(ratio[p], 1e-6f)) / Math.Log(1.25), -1.0, 1.0);
            double r = t < 0.0 ? 1.0 + t : 1.0, g = 1.0 - Math.Abs(t), b = t > 0.0 ? 1.0 - t : 1.0;
            rgb[p * 3] = (byte)Math.Round(255.0 * r);
            rgb[p * 3 + 1] = (byte)Math.Round(255.0 * g);
            rgb[p * 3 + 2] = (byte)Math.Round(255.0 * b);
        }
        return rgb;
    }

    private static int Int(Dictionary<string, string> values, string key, int fallback, List<string> problems)
    {
        if (!values.TryGetValue(key, out string? text))
            return fallback;
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0)
            return value;
        problems.Add($"{key} must be a positive whole number, got '{text}'.");
        return fallback;
    }
}
