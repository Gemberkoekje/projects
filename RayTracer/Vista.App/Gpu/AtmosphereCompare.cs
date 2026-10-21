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
/// <c>Vista.App --atmosphere-compare</c> (vista-plan §10 M1.4 verify): one view rendered three ways on the RTX 3070 — the
/// LUTs, the reference atmosphere (which shares the LUTs' model: it checks how they evaluate it), and the reference with
/// the terrain's shadow in the air (the light the LUTs cannot know about) — compared by distance band
/// (<see cref="RenderComparison"/>).
/// <code>
/// Vista.App --atmosphere-compare --recipe r.json [--camera c.json] [--seed 42] [--width 320] [--spp 1024]
///                                [--reference-spp 16384] [--out dir]
/// </code>
/// With <c>--out</c> it writes the three images at one exposure (the LUT render's), a sheet of them, and the numbers.
/// </summary>
internal static class AtmosphereCompare
{
    /// <summary>The flag that selects this mode.</summary>
    public const string Flag = "--atmosphere-compare";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --atmosphere-compare --recipe r.json [--camera c.json] [--seed 42] [--width 320] [--spp 1024]\n" +
        "                 [--reference-spp 16384] [--out dir]  (one view: LUTs, reference and shadowed reference, by distance)";

    /// <summary>Runs the comparison; returns 0, or 1 for bad arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        var problems = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Count; i++)
        {
            if (i + 1 < args.Count && args[i] is "--recipe" or "--camera" or "--seed" or "--width" or "--spp" or "--reference-spp" or "--out")
                values[args[i]] = args[++i];
            else
                problems.Add($"Unknown or valueless argument '{args[i]}'.");
        }
        if (!values.ContainsKey("--recipe"))
            problems.Add("--recipe is required.");
        ulong seed = values.TryGetValue("--seed", out string? s) && ulong.TryParse(s, CultureInfo.InvariantCulture, out ulong parsed) ? parsed : 42;
        int width = Int(values, "--width", 320, problems);
        int spp = Int(values, "--spp", 1024, problems);
        int referenceSpp = Int(values, "--reference-spp", 16384, problems);
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
        int height = Math.Max(1, (int)Math.Round(width * 9.0 / 16.0));
        TerrainMesh mesh = worlds.Mesh(MeshView.For(camera, width, height, AtmosphereModel.Default.PlanetRadiusMetres + ground), new MeshSettings());
        var scene = new VistaScene(recipe, seed, camera, terrain, mesh, almanac.Sun, MaterialCatalog.M0, AtmosphereModel.Default, ground);

        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}");
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{recipe.Name ?? "recipe"}, seed {seed}, {width}×{height}; sun {almanac.Sun.ElevationDegrees:0.0}° up at {almanac.Sun.AzimuthDegrees:0}°; base {ground:0} m ({groundFrom})"));
        RenderOutput lut = Render(renderer, scene, width, height, spp, AtmosphereMode.Luts, [Aov.Depth, Aov.Variance], log);
        RenderOutput reference = Render(renderer, scene, width, height, referenceSpp, AtmosphereMode.Reference, [Aov.Variance], log);
        RenderOutput shadowed = Render(renderer, scene, width, height, referenceSpp, AtmosphereMode.ReferenceShadowed, [Aov.Variance], log);

        float[] depth = lut.Aovs[Aov.Depth];
        IReadOnlyList<ComparisonBin> model = RenderComparison.Compare(depth, lut.Xyz, lut.Aovs[Aov.Variance], reference.Xyz, reference.Aovs[Aov.Variance]);
        IReadOnlyList<ComparisonBin> limit = RenderComparison.Compare(depth, shadowed.Xyz, shadowed.Aovs[Aov.Variance], reference.Xyz, reference.Aovs[Aov.Variance]);
        log.WriteLine("  distance   pixels   LUTs ÷ reference − 1    shadowed ÷ reference − 1 (the terrain's shadow in the air)");
        for (int i = 0; i < model.Count; i++)
        {
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {model[i].Bin,-9} {model[i].Pixels,7}   {model[i].Error,8:+0.00%;-0.00%;0.00%} ± {model[i].ErrorNoise,5:0.00%}      {limit[i].Error,8:+0.00%;-0.00%;0.00%} ± {limit[i].ErrorNoise,5:0.00%}"));
        }

        if (values.TryGetValue("--out", out string? outDir))
        {
            Directory.CreateDirectory(outDir);
            double ev = AutoExposure.Measure(lut.Xyz, camera.ExposureEv).AppliedEv;
            var tiles = new List<byte[]>();
            string[] names = ["luts", "reference", "shadowed"];
            RenderOutput[] outputs = [lut, reference, shadowed];
            for (int i = 0; i < outputs.Length; i++)
            {
                byte[] rgb = DisplayTransform.ToSrgb8(DisplayTransform.XyzToLinearRec709(outputs[i].Xyz), ev, DisplayTransform.DefaultCurve);
                PngEncoder.Write(Path.Combine(outDir, names[i] + ".png"), rgb, width, height, PngColor.Rgb);
                tiles.Add(rgb);
            }
            string[] labels =
            [
                string.Create(CultureInfo.InvariantCulture, $"LUTs, {spp} spp"),
                string.Create(CultureInfo.InvariantCulture, $"reference, {referenceSpp} spp"),
                string.Create(CultureInfo.InvariantCulture, $"reference with the terrain's shadow in the air, {referenceSpp} spp"),
            ];
            SheetLayout layout = ContactSheet.Layout([.. outputs.Select(_ => (width, height))], labels, 3,
                string.Create(CultureInfo.InvariantCulture, $"{recipe.Name ?? "recipe"} · seed {seed} · one exposure ({ev:+0.0;-0.0} EV)"));
            PngEncoder.Write(Path.Combine(outDir, "compare.png"), ContactSheet.Compose(layout, tiles), layout.Width, layout.Height, PngColor.Rgb);
            File.WriteAllText(Path.Combine(outDir, "compare.json"), VistaJson.Serialize(new AtmosphereCompareReport(
                renderer.DeviceName, recipe, seed, camera, width, height, spp, referenceSpp, ground, model, limit)));
            log.WriteLine($"images and report: {outDir}");
        }
        return 0;
    }

    private static RenderOutput Render(D3D12VistaRenderer renderer, VistaScene scene, int width, int height, int spp, AtmosphereMode mode,
        HashSet<Aov> aovs, TextWriter log)
    {
        var settings = new RenderSettings { Width = width, Height = height, SamplesPerPixel = spp, Atmosphere = mode };
        var planner = new BatchPlanner(width, height, spp, settings.Batching);
        RenderOutput output = renderer.Render(scene, settings, aovs, planner);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {mode}: {spp} spp, {output.Stats.TraceSeconds:0.0} s GPU, {output.Stats.Dispatches} dispatches"));
        return output;
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

/// <summary>What <c>--atmosphere-compare --out</c> writes.</summary>
/// <param name="Device">The GPU.</param>
/// <param name="Recipe">The recipe.</param>
/// <param name="Seed">The seed.</param>
/// <param name="Camera">The camera.</param>
/// <param name="Width">Image width.</param>
/// <param name="Height">Image height.</param>
/// <param name="SamplesPerPixel">The LUT render's samples per pixel.</param>
/// <param name="ReferenceSamplesPerPixel">The references' samples per pixel.</param>
/// <param name="GroundAltitudeMetres">The scene's base altitude.</param>
/// <param name="LutsAgainstReference">The LUTs against the reference, by distance.</param>
/// <param name="ShadowedAgainstReference">The shadowed reference against the reference, by distance: the known limit.</param>
internal sealed record AtmosphereCompareReport(
    string Device,
    Recipe Recipe,
    ulong Seed,
    CameraSpec Camera,
    int Width,
    int Height,
    int SamplesPerPixel,
    int ReferenceSamplesPerPixel,
    double GroundAltitudeMetres,
    IReadOnlyList<ComparisonBin> LutsAgainstReference,
    IReadOnlyList<ComparisonBin> ShadowedAgainstReference);
