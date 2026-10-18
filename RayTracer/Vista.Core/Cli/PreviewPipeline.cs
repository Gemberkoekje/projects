using System.Diagnostics;
using System.Globalization;
using RayTracer;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.Cli;

/// <summary>
/// The headless preview, end to end except for the GPU (vista-plan §7.4): load the recipe and camera,
/// generate the world, mesh it, hand the scene to an <see cref="IVistaRenderer"/>, and write the beauty
/// (PNG + EXR), the requested debug images and the sidecar. With no renderer (<c>--dry-run</c>) it stops
/// before the GPU and writes the sidecar plus a top-down height map, so the world side can be inspected
/// from any machine.
/// </summary>
public static class PreviewPipeline
{
    /// <summary>Runs <paramref name="request"/>; returns the sidecar that was written.</summary>
    public static Sidecar Run(PreviewRequest request, IVistaRenderer? renderer, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (renderer is null && !request.DryRun)
            throw new InvalidOperationException("A renderer is required unless the request is a dry run.");
        time ??= TimeProvider.System;

        var timings = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var outputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var stopwatch = Stopwatch.StartNew();

        Recipe recipe = VistaJson.ParseRecipe(File.ReadAllText(request.RecipePath));
        HeightField terrain = TerrainGenerator.Generate(recipe, request.Seed);
        timings["worldGen"] = Lap(stopwatch);

        TerrainMesh mesh = HeightFieldMesher.Build(terrain);
        timings["mesh"] = Lap(stopwatch);

        CameraSpec camera = request.CameraPath is null
            ? DefaultCamera.For(terrain)
            : VistaJson.ParseCamera(File.ReadAllText(request.CameraPath));
        if (DefaultCamera.GroundWarning(camera, terrain) is { } groundWarning)
            warnings.Add(groundWarning);

        SunState sun = SunPlaceholder.For(recipe.Time);
        IReadOnlyList<VistaMaterial> materials = MaterialCatalog.M0;
        var settings = new RenderSettings
        {
            Width = request.Width,
            Height = request.Height,
            SamplesPerPixel = request.SamplesPerPixel,
            MaxBounces = request.MaxBounces,
        };

        string directory = Path.GetDirectoryName(Path.GetFullPath(request.OutPath))!;
        string baseName = Path.GetFileNameWithoutExtension(request.OutPath);
        Directory.CreateDirectory(directory);
        string Sibling(string suffix) => Path.Combine(directory, baseName + suffix);

        RenderInfo? renderInfo = null;
        if (renderer is null)
        {
            (byte[] map, int size) = DebugImages.HeightMap(terrain, camera);
            PngEncoder.Write(Sibling(".heightmap.png"), map, size, size, PngColor.Rgb);
            outputs["heightMap"] = baseName + ".heightmap.png";
            timings["write"] = Lap(stopwatch);
        }
        else
        {
            var scene = new VistaScene(recipe, request.Seed, camera, terrain, mesh, sun, materials);
            var planner = new BatchPlanner(settings.Width, settings.Height, settings.SamplesPerPixel, settings.Batching);
            RenderOutput output = renderer.Render(scene, settings, AovsFor(request.Debug), planner);
            if (output.Width != settings.Width || output.Height != settings.Height)
                throw new InvalidOperationException($"Renderer returned {output.Width}×{output.Height}, expected {settings.Width}×{settings.Height}.");
            if (output.Xyz.Length != settings.Width * settings.Height * 3)
                throw new InvalidOperationException($"Renderer returned {output.Xyz.Length} XYZ floats, expected {settings.Width * settings.Height * 3}.");
            timings["render"] = Lap(stopwatch);
            renderInfo = RenderInfo.From(output.Stats);

            float[] linear = DisplayTransform.XyzToLinearRec709(output.Xyz);
            PngEncoder.Write(request.OutPath, DisplayTransform.ToSrgb8(linear, camera.ExposureEv), settings.Width, settings.Height, PngColor.Rgb);
            outputs["beauty"] = Path.GetFileName(request.OutPath);
            ExrEncoder.WriteRgb(Sibling(".exr"), linear, settings.Width, settings.Height);
            outputs["beautyExr"] = baseName + ".exr";
            foreach (DebugOutput debug in request.Debug.OrderBy(d => d))
            {
                string role = debug.ToString().ToLowerInvariant();
                byte[] rgb = DebugImage(debug, output, materials);
                PngEncoder.Write(Sibling($".{role}.png"), rgb, settings.Width, settings.Height, PngColor.Rgb);
                outputs[role] = $"{baseName}.{role}.png";
            }
            timings["write"] = Lap(stopwatch);
        }

        outputs["sidecar"] = baseName + ".json";
        var sidecar = new Sidecar
        {
            Kind = renderer is null ? "dryRun" : "preview",
            CreatedUtc = time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Engine = EngineInfo.Current(renderer?.DeviceName),
            Recipe = recipe,
            Seed = request.Seed,
            Camera = camera,
            DefaultCamera = request.CameraPath is null,
            Image = new ImageInfo(settings.Width, settings.Height, settings.SamplesPerPixel, settings.MaxBounces, DisplayTransform.Name),
            World = new WorldInfo(
                TerrainProfile.For(recipe.SetPieces).Name,
                terrain.Size,
                terrain.CellSize,
                terrain.ContentHash().ToString("x16", CultureInfo.InvariantCulture),
                terrain.MinHeight,
                terrain.MaxHeight,
                mesh.TriangleCount,
                mesh.ByteSize),
            Sun = new SunInfo(
                SunPlaceholder.Source,
                new WorldPoint(sun.Direction.X, sun.Direction.Y, sun.Direction.Z),
                SunPlaceholder.ElevationDegrees(sun),
                sun.TempK),
            DataTiers = MaterialCatalog.TiersOf(materials),
            Render = renderInfo,
            TimingsSeconds = timings,
            Outputs = outputs,
            Warnings = warnings,
        };
        File.WriteAllText(Sibling(".json"), VistaJson.Serialize(sidecar));
        return sidecar;
    }

    /// <summary>The renderer outputs <paramref name="debug"/> needs (the tier map is derived from material ids).</summary>
    public static IReadOnlySet<Aov> AovsFor(IEnumerable<DebugOutput> debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
        var aovs = new HashSet<Aov>();
        foreach (DebugOutput d in debug)
        {
            aovs.Add(d switch
            {
                DebugOutput.Albedo => Aov.Albedo,
                DebugOutput.Normal => Aov.Normal,
                DebugOutput.Depth => Aov.Depth,
                DebugOutput.Material or DebugOutput.Tier => Aov.MaterialId,
                DebugOutput.Variance => Aov.Variance,
                _ => throw new ArgumentOutOfRangeException(nameof(debug), d, "Unknown debug output."),
            });
        }
        return aovs;
    }

    private static byte[] DebugImage(DebugOutput debug, RenderOutput output, IReadOnlyList<VistaMaterial> materials)
    {
        float[] Buffer(Aov aov)
        {
            if (!output.Aovs.TryGetValue(aov, out float[]? data))
                throw new InvalidOperationException($"Renderer did not return the {aov} output.");
            int expected = output.Width * output.Height * AovLayout.Channels(aov);
            if (data.Length != expected)
                throw new InvalidOperationException($"Renderer returned {data.Length} floats for {aov}, expected {expected}.");
            return data;
        }

        return debug switch
        {
            DebugOutput.Albedo => DebugImages.Albedo(Buffer(Aov.Albedo)),
            DebugOutput.Normal => DebugImages.Normal(Buffer(Aov.Normal)),
            DebugOutput.Depth => DebugImages.Depth(Buffer(Aov.Depth)),
            DebugOutput.Material => DebugImages.Material(Buffer(Aov.MaterialId)),
            DebugOutput.Tier => DebugImages.Tier(Buffer(Aov.MaterialId), materials),
            DebugOutput.Variance => DebugImages.Variance(Buffer(Aov.Variance)),
            _ => throw new ArgumentOutOfRangeException(nameof(debug), debug, "Unknown debug output."),
        };
    }

    private static double Lap(Stopwatch stopwatch)
    {
        double seconds = stopwatch.Elapsed.TotalSeconds;
        stopwatch.Restart();
        return seconds;
    }
}
