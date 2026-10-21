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
/// <c>Vista.App --height-field-compare</c> (vista-plan §10 M1.10): the far field meshed (M1.9) against traced as height-field tiles,
/// on fixed views, at each error target (the mesh over the world grid is the same for both). Three parts. <b>Size:</b> at the cost
/// size, each view's mesh and its height field at each tile size: triangles, tiles, the bytes of the mesh, the tiles and the far
/// grids, the acceleration structures, the process's video memory, the CPU time to refine, place and lay out the tiles, the GPU
/// time to upload and build (the far grids' upload, once per world, apart), and the cost per pixel-sample. <b>Cost:</b> the two
/// rendered alternately by two renderers, round after round, so a GPU shared with other programs slows both alike; the median
/// ratio is the number to read. <b>Image:</b> at the image size, the mesh against the height field at each image error target,
/// against the 1 px mesh on another random stream (the noise floor), and the first tile size against the second: mean |ΔY| over
/// the pixels whose first hit lies beyond the world grid and over all, and the far pixels' mean luminance, with difference images.
/// <code>
/// Vista.App --height-field-compare --cameras a.json,b.json [--recipe r.json] [--seed 42] [--size 3840] [--errors 1,0.5]
///                                  [--tiles 8,16] [--rounds 3] [--spp 16] [--image-width 3840] [--image-spp 128]
///                                  [--image-errors 2,1,0.5] [--image-scale 3] [--out dir]
/// </code>
/// </summary>
internal static class HeightFieldCompare
{
    /// <summary>The flag that selects this mode.</summary>
    public const string Flag = "--height-field-compare";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --height-field-compare --cameras a.json,b.json [--recipe r.json] [--seed 42] [--size 3840] [--errors 1,0.5]\n" +
        "                 [--tiles 8,16] [--rounds 3] [--spp 16] [--image-width 3840] [--image-spp 128] [--image-errors 2,1,0.5]\n" +
        "                 [--image-scale 3] [--out dir]\n" +
        "                 (the far field meshed against traced as a height field: memory, build time, cost, image difference)";

    /// <summary>A difference of this much of the mean luminance is white in the difference images.</summary>
    private const double DifferenceScale = 0.25;

    /// <summary>Runs the comparison; returns 0, or 1 for bad arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        string? recipePath = null, outDir = null;
        var cameras = new List<string>();
        ulong seed = 42;
        int size = 3840, rounds = 3, spp = 16, imageWidth = 3840, imageSpp = 128, imageScale = 3;
        var tiles = new List<int> { FarHeightField.DefaultTileCells, 16 };
        var errors = new List<double> { 1.0, 0.5 };
        var imageErrors = new List<double> { 2.0, 1.0, 0.5 };
        for (int i = 1; i < args.Count; i++)
        {
            string arg = args[i];
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
            int NextInt() => int.Parse(Next(), CultureInfo.InvariantCulture);
            List<double> Skip()
            {
                i++;
                return [];
            }
            List<double> NextDoubles() => [.. Next().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, CultureInfo.InvariantCulture))];
            switch (arg)
            {
                case "--recipe": recipePath = Next(); break;
                case "--out": outDir = Next(); break;
                case "--cameras": cameras = [.. Next().Split(',', StringSplitOptions.RemoveEmptyEntries)]; break;
                case "--seed": seed = ulong.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--size": size = NextInt(); break;
                case "--errors": errors = NextDoubles(); break;
                case "--tiles": tiles = [.. Next().Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture))]; break;
                case "--rounds": rounds = NextInt(); break;
                case "--spp": spp = NextInt(); break;
                case "--image-width": imageWidth = NextInt(); break;
                case "--image-spp": imageSpp = NextInt(); break;
                case "--image-errors": imageErrors = args[i + 1] == "none" ? Skip() : NextDoubles(); break;
                case "--image-scale": imageScale = NextInt(); break;
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
        log.WriteLine($"Device: {renderer.DeviceName}, driver {renderer.DriverVersion}; {recipe.Name ?? "recipe"}, seed {seed}");
        var worlds = new WorldCache();
        TerrainWorld world = worlds.World(recipe, seed, WorldGrid.Default, renderer, ErosionCache.Default, erode: true);
        Almanac almanac = Almanac.For(recipe);
        double ground = Places.BaseAltitude(recipe, almanac.Place).Metres;
        double radius = AtmosphereModel.Default.PlanetRadiusMetres + ground;
        static MeshSettings MeshAt(double error) => new() { ErrorPixels = error };
        static MeshSettings FieldAt(double error, int tileCells) => new() { ErrorPixels = error, HeightField = true, HeightFieldTileCells = tileCells };
        var views = cameras.Select(path => (Name: Path.GetFileNameWithoutExtension(path).Replace(".camera", string.Empty, StringComparison.Ordinal),
            Camera: VistaJson.ParseCamera(File.ReadAllText(path)))).ToList();
        int height = Height(size);
        VistaScene Scene(CameraSpec camera, TerrainMesh m) =>
            new(recipe, seed, camera, world.Surface, m, almanac.Sun, MaterialCatalog.M0, AtmosphereModel.Default, ground);
        TerrainMesh Mesh(CameraSpec camera, int width, MeshSettings settings) => worlds.Mesh(MeshView.For(camera, width, Height(width), radius), settings);
        var report = new StringBuilder();
        void Line(string text)
        {
            log.WriteLine(text);
            report.AppendLine(text);
        }

        // 1. Size: one renderer, one scene at a time, so its video memory is that scene's.
        Line($"Size at {size}×{height}, {spp} spp:");
        Line("");
        Line("| View | Error | Far field | Triangles (beyond the grid) | Tiles | Mesh | Tiles (+ far grids) | BLAS | VRAM | Refine + place + tiles (CPU) | Upload + BLAS | ns per pixel-sample |");
        Line("|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach ((string name, CameraSpec camera) in views)
        {
            foreach (double error in errors)
            {
                foreach ((string label, MeshSettings settings) in tiles.Select(t => ($"height field, {t}² tiles", FieldAt(error, t))).Prepend(("mesh", MeshAt(error))))
                {
                    TerrainMesh m = Mesh(camera, size, settings);
                    RenderOutput output = Render(renderer, Scene(camera, m), size, spp, 0u, false);
                    MeshInfo info = m.Info;
                    RenderStats stats = output.Stats;
                    double build = stats.StageSeconds.GetValueOrDefault(RenderStageKeys.MeshUpload) + stats.StageSeconds.GetValueOrDefault(RenderStageKeys.AccelerationBuild);
                    Line(string.Create(CultureInfo.InvariantCulture,
                        $"| {name} | {error:0.##} px | {label} | {info.Triangles / 1e6:0.00} M ({info.FarTriangles / 1e6:0.00}) | {info.HeightFieldTiles:N0} | " +
                        $"{info.Bytes / 1048576.0:0} MiB | {info.HeightFieldBytes / 1048576.0:0.0} (+ {info.HeightFieldGridBytes / 1048576.0:0}) MiB | " +
                        $"{stats.AccelerationStructureBytes / 1048576.0:0} MiB | {stats.VideoMemoryBytes / 1048576.0:0} MiB | " +
                        $"{info.RefineSeconds:0.00} + {info.VertexSeconds:0.00} + {info.HeightFieldSeconds:0.00} s | {build:0.00} s | " +
                        $"{stats.TraceSeconds * 1e9 / stats.PixelSamples:0.00} |"));
                    if (m.HeightField.Any)
                    {
                        string upload = stats.StageSeconds.TryGetValue(D3D12VistaRenderer.HeightFieldGridsStage, out double seconds)
                            ? string.Create(CultureInfo.InvariantCulture, $" and {seconds:0.00} s to upload")
                            : string.Empty;
                        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                            $"    {m.HeightField.Level1Tiles:N0} g1 + {m.HeightField.Level2Tiles:N0} g2 tiles; grid mismatch {m.HeightField.GridMismatch:0.0000} of a cell, " +
                            $"margin {m.HeightField.Margin:0.000}; the far grids took {m.HeightField.Grids.BuildSeconds:0.00} s once{upload}"));
                    }
                }
            }
        }

        // 2. Cost: two renderers keep their scenes, rendered alternately.
        Line("");
        Line($"Cost at {size}×{height}, {spp} spp, {rounds} rounds, alternately (ns per pixel-sample; ratio = height field / mesh):");
        Line("");
        Line("| View | Error | Far field | Mesh, ns | Height field, ns | Ratio, median (range) |");
        Line("|---|---|---|---|---|---|");
        using (var second = new D3D12VistaRenderer())
        {
            foreach ((string name, CameraSpec camera) in views)
            {
                foreach (double error in errors)
                {
                    VistaScene meshScene = Scene(camera, Mesh(camera, size, MeshAt(error)));
                    foreach (int tileCells in tiles)
                    {
                        VistaScene fieldScene = Scene(camera, Mesh(camera, size, FieldAt(error, tileCells)));
                        Render(renderer, meshScene, size, spp, 0u, false); // build both scenes before timing
                        Render(second, fieldScene, size, spp, 0u, false);
                        var meshNs = new List<double>();
                        var fieldNs = new List<double>();
                        for (int round = 0; round < rounds; round++)
                        {
                            // Alternate which goes first, so a load that changes mid-round falls on both.
                            bool meshFirst = round % 2 == 0;
                            double a = Cost(meshFirst ? renderer : second, meshFirst ? meshScene : fieldScene, size, spp);
                            double b = Cost(meshFirst ? second : renderer, meshFirst ? fieldScene : meshScene, size, spp);
                            meshNs.Add(meshFirst ? a : b);
                            fieldNs.Add(meshFirst ? b : a);
                        }
                        double[] ratios = [.. meshNs.Zip(fieldNs, (m, f) => f / m).Order()];
                        Line(string.Create(CultureInfo.InvariantCulture,
                            $"| {name} | {error:0.##} px | {tileCells}² tiles | {Median(meshNs):0.00} | {Median(fieldNs):0.00} | " +
                            $"{Median([.. ratios]):0.000} ({ratios[0]:0.000}–{ratios[^1]:0.000}) |"));
                    }
                }
            }
        }

        // 3. Image: at each error target the mesh against the height field (the same mesh over the world grid, the far field apart);
        // the 1 px mesh against itself on another stream; the second tile size against the first. None for --image-errors none.
        if (imageErrors.Count == 0)
        {
            if (outDir is not null)
                File.WriteAllText(Path.Combine(outDir, "height-field-compare.md"), report.ToString());
            return 0;
        }
        int imageHeight = Height(imageWidth);
        Line("");
        Line($"Image at {imageWidth}×{imageHeight}, {imageSpp} spp: mean |ΔY| as a share of the mean, beyond the grid / over all pixels " +
            "(and the far pixels' mean luminance against the height field's):");
        Line("");
        Line("| View | Pixels beyond the grid | " + string.Join(" | ", imageErrors.Select(e => string.Create(CultureInfo.InvariantCulture, $"Mesh against height field, {e:0.##} px"))) +
            " | Noise floor (mesh 1 px, two streams) | " + (tiles.Count > 1 ? $"{tiles[1]}² against {tiles[0]}² tiles" : "—") + " |");
        Line("|---|---|" + string.Concat(imageErrors.Select(_ => "---|")) + "---|---|");
        foreach ((string name, CameraSpec camera) in views)
        {
            RenderOutput mesh1 = Render(renderer, Scene(camera, Mesh(camera, imageWidth, MeshAt(1.0))), imageWidth, imageSpp, 0u, true);
            bool[] far = FarMask(mesh1, camera, MeshView.For(camera, imageWidth, imageHeight, radius).Frame, world.Surface, imageWidth, imageHeight);
            string Cell(RenderOutput a, RenderOutput b)
            {
                (double farDiff, double allDiff, double bias) = Difference(a.Xyz, b.Xyz, far);
                return string.Create(CultureInfo.InvariantCulture, $"{farDiff:0.00%} / {allDiff:0.00%} ({bias:+0.00%;-0.00%})");
            }
            var cells = new List<string>();
            var fieldAt = new Dictionary<double, RenderOutput>();
            foreach (double error in imageErrors)
            {
                RenderOutput field = Render(renderer, Scene(camera, Mesh(camera, imageWidth, FieldAt(error, tiles[0]))), imageWidth, imageSpp, 0u, false);
                RenderOutput mesh = error == 1.0 ? mesh1 : Render(renderer, Scene(camera, Mesh(camera, imageWidth, MeshAt(error))), imageWidth, imageSpp, 0u, false);
                cells.Add(Cell(mesh, field));
                fieldAt[error] = field;
                if (outDir is not null)
                {
                    string tag = string.Create(CultureInfo.InvariantCulture, $"{error:0.##}px").Replace('.', '_');
                    double ev = AutoExposure.Measure(field.Xyz, camera.ExposureEv).AppliedEv;
                    WriteImage(Path.Combine(outDir, $"{name}-{tag}-height-field.png"), field.Xyz, imageWidth, imageHeight, imageScale, ev);
                    WriteImage(Path.Combine(outDir, $"{name}-{tag}-mesh.png"), mesh.Xyz, imageWidth, imageHeight, imageScale, ev);
                    WriteDifference(Path.Combine(outDir, $"{name}-{tag}-diff.png"), mesh.Xyz, field.Xyz, imageWidth, imageHeight, imageScale);
                }
            }
            RenderOutput mesh1b = Render(renderer, Scene(camera, Mesh(camera, imageWidth, MeshAt(1.0))), imageWidth, imageSpp, 1u, false);
            string tileCell = "—";
            if (tiles.Count > 1 && fieldAt.TryGetValue(1.0, out RenderOutput? field1))
                tileCell = Cell(Render(renderer, Scene(camera, Mesh(camera, imageWidth, FieldAt(1.0, tiles[1]))), imageWidth, imageSpp, 0u, false), field1);
            Line(string.Create(CultureInfo.InvariantCulture,
                $"| {name} | {far.Count(f => f) / (double)far.Length:0.0%} | {string.Join(" | ", cells)} | {Cell(mesh1, mesh1b)} | {tileCell} |"));
            if (outDir is not null)
                WriteDifference(Path.Combine(outDir, $"{name}-noise-diff.png"), mesh1.Xyz, mesh1b.Xyz, imageWidth, imageHeight, imageScale);
        }
        if (outDir is not null)
            File.WriteAllText(Path.Combine(outDir, "height-field-compare.md"), report.ToString());
        return 0;
    }

    private static int Height(int width) => Math.Max(1, (int)Math.Round(width * 9.0 / 16.0));

    private static RenderOutput Render(D3D12VistaRenderer renderer, VistaScene scene, int width, int spp, uint stream, bool depth)
    {
        int height = Height(width);
        var settings = new RenderSettings { Width = width, Height = height, SamplesPerPixel = spp, MaxBounces = 6, RandomStream = stream };
        HashSet<Aov> aovs = depth ? [Aov.Depth] : [];
        return renderer.Render(scene, settings, aovs, new BatchPlanner(width, height, spp, settings.Batching));
    }

    private static double Cost(D3D12VistaRenderer renderer, VistaScene scene, int width, int spp)
    {
        RenderStats stats = Render(renderer, scene, width, spp, 0u, false).Stats;
        return stats.TraceSeconds * 1e9 / stats.PixelSamples;
    }

    private static double Median(List<double> values)
    {
        double[] sorted = [.. values.Order()];
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
    }

    /// <summary>
    /// The pixels whose first hit (sample 0's, through the pixel's centre near enough) lies beyond the world grid's square: the
    /// far field the two paths render differently.
    /// </summary>
    private static bool[] FarMask(RenderOutput withDepth, CameraSpec camera, EarthFrame frame, HeightField grid, int width, int height)
    {
        float[] depth = withDepth.Aovs[Aov.Depth];
        double tanHalf = Math.Tan(0.5 * camera.VerticalFovRadians()), aspect = (double)width / height;
        Vector3D forward = camera.Forward(), right = camera.Right(), up = camera.Up();
        Vector3D eye = frame.ToFrame(camera.Position.X, camera.Position.Y, camera.Position.Z);
        double low = grid.OriginX, high = grid.OriginX + grid.Extent;
        var far = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double t = depth[y * width + x];
                if (!double.IsFinite(t) || t <= 0.0)
                    continue;
                double px = (2.0 * ((x + 0.5) / width) - 1.0) * tanHalf * aspect, py = (1.0 - 2.0 * ((y + 0.5) / height)) * tanHalf;
                Vector3D p = eye + (forward + right * px + up * py).Normalized() * t;
                double horizontal = Math.Sqrt(p.X * p.X + p.Z * p.Z);
                double s = horizontal > 0.0 ? frame.GroundRadius * Math.Atan2(horizontal, p.Y + frame.GroundRadius) / horizontal : 0.0;
                double wx = frame.TangentX + p.X * s, wz = frame.TangentZ + p.Z * s;
                far[y * width + x] = wx < low || wx > high || wz < low || wz > high;
            }
        }
        return far;
    }

    /// <summary>Mean |ΔY| over the far pixels and over all, each of <paramref name="b"/>'s mean there, and the far pixels' mean luminance of a against b.</summary>
    private static (double Far, double All, double FarBias) Difference(float[] a, float[] b, bool[] far)
    {
        double farA = 0.0, farB = 0.0, farAbs = 0.0, allB = 0.0, allAbs = 0.0;
        for (int p = 0; p < far.Length; p++)
        {
            double ya = a[p * 3 + 1], yb = b[p * 3 + 1];
            allB += yb;
            allAbs += Math.Abs(ya - yb);
            if (far[p])
            {
                farA += ya;
                farB += yb;
                farAbs += Math.Abs(ya - yb);
            }
        }
        return (farB > 0.0 ? farAbs / farB : 0.0, allB > 0.0 ? allAbs / allB : 0.0, farB > 0.0 ? farA / farB - 1.0 : 0.0);
    }

    /// <summary><paramref name="xyz"/> box-filtered down by <paramref name="scale"/> and written as a PNG at <paramref name="ev"/>.</summary>
    private static void WriteImage(string path, float[] xyz, int width, int height, int scale, double ev)
    {
        (float[] small, int w, int h) = Downsample(xyz, width, height, scale);
        PngEncoder.Write(path, DisplayTransform.ToSrgb8(DisplayTransform.XyzToLinearRec709(small), ev), w, h, PngColor.Rgb);
    }

    /// <summary>
    /// |ΔY| of each pixel as a share of b's mean luminance, box-filtered down by <paramref name="scale"/>: black, red, yellow, white
    /// at <see cref="DifferenceScale"/>.
    /// </summary>
    private static void WriteDifference(string path, float[] a, float[] b, int width, int height, int scale)
    {
        int pixels = width * height;
        double mean = 0.0;
        for (int p = 0; p < pixels; p++)
            mean += b[p * 3 + 1];
        mean = Math.Max(mean / pixels, 1e-30);
        var difference = new float[pixels * 3];
        for (int p = 0; p < pixels; p++)
            difference[p * 3] = difference[p * 3 + 1] = difference[p * 3 + 2] = (float)(Math.Abs(a[p * 3 + 1] - b[p * 3 + 1]) / mean);
        (float[] small, int w, int h) = Downsample(difference, width, height, scale);
        var rgb = new byte[w * h * 3];
        for (int p = 0; p < w * h; p++)
        {
            double v = Math.Clamp(small[p * 3] / DifferenceScale, 0.0, 1.0);
            rgb[p * 3] = (byte)Math.Round(255.0 * Math.Clamp(3.0 * v, 0.0, 1.0));
            rgb[p * 3 + 1] = (byte)Math.Round(255.0 * Math.Clamp(3.0 * v - 1.0, 0.0, 1.0));
            rgb[p * 3 + 2] = (byte)Math.Round(255.0 * Math.Clamp(3.0 * v - 2.0, 0.0, 1.0));
        }
        PngEncoder.Write(path, rgb, w, h, PngColor.Rgb);
    }

    private static (float[] Pixels, int Width, int Height) Downsample(float[] rgb, int width, int height, int scale)
    {
        if (scale <= 1)
            return (rgb, width, height);
        int w = width / scale, h = height / scale;
        var small = new float[w * h * 3];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    double sum = 0.0;
                    for (int dy = 0; dy < scale; dy++)
                    {
                        for (int dx = 0; dx < scale; dx++)
                            sum += rgb[((y * scale + dy) * width + x * scale + dx) * 3 + c];
                    }
                    small[(y * w + x) * 3 + c] = (float)(sum / (scale * scale));
                }
            }
        }
        return (small, w, h);
    }
}
