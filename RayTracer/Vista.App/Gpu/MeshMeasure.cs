using System.Globalization;
using System.Text;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --mesh-measure</c> (vista-plan §10 M1.9): the terrain meshes of fixed views at fixed sizes, measured — for the
/// uniform mesh of the world grid (M1.7's, bent) and the view-dependent mesh at each error target: triangles (those below the grid
/// and beyond it apart), vertices, the mesh's and the acceleration structure's bytes and the process's video memory, the time to
/// refine and place the mesh on the CPU and to upload it and build its acceleration structure on the GPU, and the GPU's cost per
/// pixel-sample. Each view's world hierarchy is built once and reported too. With <c>--out</c> every render is written as a PNG.
/// <code>
/// Vista.App --mesh-measure --cameras a.json,b.json [--recipe r.json] [--seed 42] [--sizes 320,3840] [--errors 0.5,1,2]
///                          [--uniform] [--plain] [--downsample] [--spp 16] [--out dir]
/// </code>
/// <c>--plain</c> adds each error target without the detail or the far field: the grid alone, the uniform mesh's world, so the
/// level of detail's own cost and look can be read apart from what the detail and the far field add. <c>--downsample</c> writes each
/// view mesh's larger renders box-filtered down to the smallest size, beside it at one exposure, and how far they lie apart.
/// The GPU is often shared with other programs, so its times are best read as ratios between rows of one run.
/// </summary>
internal static class MeshMeasure
{
    /// <summary>The flag that selects this mode.</summary>
    public const string Flag = "--mesh-measure";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --mesh-measure --cameras a.json,b.json [--recipe r.json] [--seed 42] [--sizes 320,3840] [--errors 0.5,1,2]\n" +
        "                 [--uniform] [--plain] [--downsample] [--spp 16] [--out dir]\n" +
        "                 (the terrain mesh of each view and size: triangles, memory, build time, GPU cost per pixel-sample)";

    /// <summary>Runs the measurement; returns 0, or 1 for bad arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        string? recipePath = null, outDir = null;
        var cameras = new List<string>();
        ulong seed = 42;
        var sizes = new List<int> { 320, 3840 };
        var errors = new List<double> { 0.5, 1.0, 2.0 };
        bool uniform = false, plain = false, downsample = false;
        int spp = 16;
        for (int i = 1; i < args.Count; i++)
        {
            string arg = args[i];
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
            switch (arg)
            {
                case "--recipe": recipePath = Next(); break;
                case "--out": outDir = Next(); break;
                case "--cameras": cameras = [.. Next().Split(',', StringSplitOptions.RemoveEmptyEntries)]; break;
                case "--seed": seed = ulong.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--sizes": sizes = [.. Next().Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture))]; break;
                case "--errors": errors = [.. Next().Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture))]; break;
                case "--uniform": uniform = true; break;
                case "--plain": plain = true; break;
                case "--downsample": downsample = true; break;
                case "--spp": spp = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                default:
                    log.WriteLine($"Unknown argument '{arg}'.");
                    log.WriteLine(Usage);
                    return 1;
            }
        }
        if (cameras.Count == 0)
        {
            log.WriteLine("--cameras is required.");
            log.WriteLine(Usage);
            return 1;
        }
        if (outDir is not null)
            Directory.CreateDirectory(outDir);

        Recipe recipe = recipePath is null ? new Recipe { SetPieces = [SetPiece.AlpineRange] } : VistaJson.ParseRecipe(File.ReadAllText(recipePath));
        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}, driver {renderer.DriverVersion}; {recipe.Name ?? "recipe"}, seed {seed}, {spp} spp");
        var worlds = new WorldCache();
        TerrainWorld world = worlds.World(recipe, seed, WorldGrid.Default, renderer, ErosionCache.Default, erode: true);
        Almanac almanac = Almanac.For(recipe);
        double ground = Places.BaseAltitude(recipe, almanac.Place).Metres;
        double radius = AtmosphereModel.Default.PlanetRadiusMetres + ground;

        // Grouped by the hierarchy they need (with the detail, or without for the uniform and the plain meshes), so each world's is
        // built twice at most.
        var variants = new List<(string Name, MeshSettings Settings)>();
        foreach (double error in errors)
            variants.Add((string.Create(CultureInfo.InvariantCulture, $"view {error:0.##} px"), new MeshSettings { ErrorPixels = error }));
        if (uniform)
            variants.Add(("uniform", new MeshSettings { Kind = MeshKind.Uniform }));
        if (plain)
        {
            foreach (double error in errors)
            {
                variants.Add((string.Create(CultureInfo.InvariantCulture, $"view {error:0.##} px, grid only"),
                    new MeshSettings { ErrorPixels = error, Detail = false, FarField = false }));
            }
        }

        var table = new StringBuilder();
        table.AppendLine("| View | Size | Mesh | Triangles (detail / far) | Vertices | Mesh | BLAS | VRAM | Refine + place (CPU) | Upload + BLAS | ns per pixel-sample |");
        table.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach ((string name, MeshSettings settings) in variants)
        {
            foreach (string cameraPath in cameras)
            {
                CameraSpec camera = VistaJson.ParseCamera(File.ReadAllText(cameraPath));
                string view = Path.GetFileNameWithoutExtension(cameraPath).Replace(".camera", string.Empty, StringComparison.Ordinal);
                var images = new List<RenderOutput>();
                foreach (int width in sizes)
                {
                    int height = Math.Max(1, (int)Math.Round(width * 9.0 / 16.0));
                    TerrainMesh mesh = worlds.Mesh(MeshView.For(camera, width, height, radius), settings);
                    MeshInfo info = mesh.Info;
                    if (info.HierarchySeconds > 0.0)
                        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  the world's hierarchy: {info.HierarchySeconds:0.00} s"));
                    var scene = new VistaScene(recipe, seed, camera, world.Surface, mesh, almanac.Sun, MaterialCatalog.M0, AtmosphereModel.Default, ground);
                    var render = new RenderSettings { Width = width, Height = height, SamplesPerPixel = spp, MaxBounces = 6 };
                    RenderOutput output = renderer.Render(scene, render, new HashSet<Aov>(), new BatchPlanner(width, height, spp, render.Batching));
                    RenderStats stats = output.Stats;
                    double build = stats.StageSeconds.GetValueOrDefault(RenderStageKeys.MeshUpload) + stats.StageSeconds.GetValueOrDefault(RenderStageKeys.AccelerationBuild);
                    string row = string.Create(CultureInfo.InvariantCulture,
                        $"| {view} | {width}×{height} | {name} | {info.Triangles / 1e6:0.00} M ({info.DetailTriangles / 1e6:0.00} / {info.FarTriangles / 1e6:0.00}) | " +
                        $"{info.Vertices / 1e6:0.00} M | {info.Bytes / 1048576.0:0} MiB | {stats.AccelerationStructureBytes / 1048576.0:0} MiB | " +
                        $"{stats.VideoMemoryBytes / 1048576.0:0} MiB | {info.RefineSeconds:0.00} + {info.VertexSeconds:0.00} s | {build:0.00} s | " +
                        $"{stats.TraceSeconds * 1e9 / stats.PixelSamples:0.00} |");
                    table.AppendLine(row);
                    log.WriteLine(row);
                    images.Add(output);
                    if (outDir is not null)
                    {
                        float[] linear = DisplayTransform.XyzToLinearRec709(output.Xyz);
                        double ev = AutoExposure.Measure(output.Xyz, camera.ExposureEv).AppliedEv;
                        string file = Path.Combine(outDir, string.Create(CultureInfo.InvariantCulture, $"{view}-{width}-{name.Replace(' ', '-')}.png"));
                        PngEncoder.Write(file, DisplayTransform.ToSrgb8(linear, ev), width, height, PngColor.Rgb);
                    }
                }
                if (downsample && outDir is not null && settings.Kind == MeshKind.View)
                    Downsampled(images, camera, Path.Combine(outDir, $"{view}-{name.Replace(' ', '-')}"), log);
            }
        }
        if (outDir is not null)
            File.WriteAllText(Path.Combine(outDir, "mesh-measure.md"), table.ToString());
        return 0;
    }

    /// <summary>
    /// The same world at every size (vista-plan §10 M1.9): each larger render box-filtered down to the smallest size and written
    /// beside the smallest, both at the smallest's exposure, with how far their luminance lies apart — the larger render's mesh is
    /// finer, but the world is the same.
    /// </summary>
    private static void Downsampled(List<RenderOutput> images, CameraSpec camera, string prefix, TextWriter log)
    {
        RenderOutput small = images.MinBy(i => i.Width)!;
        double ev = AutoExposure.Measure(small.Xyz, camera.ExposureEv).AppliedEv;
        PngEncoder.Write(string.Create(CultureInfo.InvariantCulture, $"{prefix}-{small.Width}.png"),
            DisplayTransform.ToSrgb8(DisplayTransform.XyzToLinearRec709(small.Xyz), ev), small.Width, small.Height, PngColor.Rgb);
        foreach (RenderOutput large in images.Where(i => i.Width > small.Width))
        {
            int f = large.Width / small.Width;
            if (f * small.Width != large.Width || f * small.Height != large.Height)
                continue;
            var down = new float[small.Xyz.Length];
            for (int y = 0; y < small.Height; y++)
            {
                for (int x = 0; x < small.Width; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double sum = 0.0;
                        for (int dy = 0; dy < f; dy++)
                        {
                            for (int dx = 0; dx < f; dx++)
                                sum += large.Xyz[((y * f + dy) * large.Width + x * f + dx) * 3 + c];
                        }
                        down[(y * small.Width + x) * 3 + c] = (float)(sum / (f * f));
                    }
                }
            }
            PngEncoder.Write(string.Create(CultureInfo.InvariantCulture, $"{prefix}-{large.Width}-down.png"),
                DisplayTransform.ToSrgb8(DisplayTransform.XyzToLinearRec709(down), ev), small.Width, small.Height, PngColor.Rgb);
            double meanSmall = 0.0, meanDown = 0.0, absolute = 0.0;
            int pixels = small.Width * small.Height;
            for (int k = 0; k < pixels; k++)
            {
                double a = small.Xyz[k * 3 + 1], b = down[k * 3 + 1];
                meanSmall += a;
                meanDown += b;
                absolute += Math.Abs(a - b);
            }
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {Path.GetFileName(prefix)}: {large.Width} px box-filtered to {small.Width} px against {small.Width} px: mean luminance {meanDown / meanSmall - 1.0:+0.00%;-0.00%}, " +
                $"mean |ΔY| {absolute / meanSmall:0.00%} of the mean"));
        }
    }
}
