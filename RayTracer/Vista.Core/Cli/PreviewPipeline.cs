using System.Diagnostics;
using System.Globalization;
using Vista.Astronomy;
using Vista.Atmosphere;
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
/// from any machine. A loupe request renders only its window of the frame; the files are window-sized and the
/// sidecar records the window.
///
/// <para>Since M1.3 the scene sits in a spectral atmosphere at its base altitude — the recipe's altitude, else its
/// place's valley floor (<see cref="Places.BaseAltitude"/>, <see cref="AtmosphereFrame"/>) — the EXR is in physical
/// units, and the PNG is metered (<see cref="AutoExposure"/>, the camera's exposure as compensation).</para>
///
/// <para>Since M1.6 a request may also denoise (<see cref="PreviewRequest.Denoise"/>): the raw beauty is written as always, and
/// the denoised one beside it (<c>&lt;out&gt;.denoised.png</c> at the same exposure, <c>&lt;out&gt;.denoised.exr</c>). A denoiser that
/// cannot run leaves a warning and the raw files only.</para>
/// </summary>
public static class PreviewPipeline
{
    /// <summary>
    /// Runs <paramref name="request"/>; returns the sidecar that was written. Renders that share a world (the
    /// cells of a contact sheet) pass one <paramref name="worlds"/> cache so it is generated and meshed once. A request that
    /// denoises needs a <paramref name="denoiser"/>.
    /// </summary>
    public static Sidecar Run(PreviewRequest request, IVistaRenderer? renderer, TimeProvider? time = null, WorldCache? worlds = null,
        IDenoiser? denoiser = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (renderer is null && !request.DryRun)
            throw new InvalidOperationException("A renderer is required unless the request is a dry run.");
        if (renderer is not null && request.Denoise is not null && denoiser is null)
            throw new InvalidOperationException("A denoiser is required when the request denoises.");
        time ??= TimeProvider.System;

        var timings = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var outputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var stopwatch = Stopwatch.StartNew();

        Recipe recipe = VistaJson.ParseRecipe(JsonOverrides.Apply(File.ReadAllText(request.RecipePath), request.RecipeOverrides));
        HeightField terrain = worlds?.Terrain(recipe, request.Seed) ?? TerrainGenerator.Generate(recipe, request.Seed);
        timings["worldGen"] = Lap(stopwatch);

        TerrainMesh mesh = worlds?.Mesh(terrain) ?? HeightFieldMesher.Build(terrain);
        timings["mesh"] = Lap(stopwatch);

        CameraSpec camera = request.CameraPath is null
            ? DefaultCamera.For(terrain)
            : VistaJson.ParseCamera(File.ReadAllText(request.CameraPath));
        if (request.CameraOverrides.Count > 0)
            camera = VistaJson.ParseCamera(JsonOverrides.Apply(VistaJson.Serialize(camera), request.CameraOverrides));
        if (DefaultCamera.GroundWarning(camera, terrain) is { } groundWarning)
            warnings.Add(groundWarning);

        Almanac almanac = Almanac.For(recipe);
        warnings.AddRange(almanac.Hints);
        IReadOnlyList<VistaMaterial> materials = MaterialCatalog.M0;
        AtmosphereModel air = AtmosphereModel.Default;
        (double groundAltitude, string altitudeFrom) = Places.BaseAltitude(recipe, almanac.Place);
        AtmosphereFrame frame = VistaScene.FrameFor(air, groundAltitude, almanac.Sun, camera, terrain);
        var settings = new RenderSettings
        {
            Width = request.Width,
            Height = request.Height,
            SamplesPerPixel = request.SamplesPerPixel,
            MaxBounces = request.MaxBounces,
            Batching = new BatchPlannerOptions { DispatchBudgetMs = request.DispatchBudgetMs },
            Window = request.Loupe,
            Atmosphere = request.Atmosphere,
            LightSampling = request.LightSampling,
            RussianRouletteStart = request.RussianRouletteStart,
        };
        PixelWindow window = settings.Rendered;
        if (!window.FitsIn(settings.Width, settings.Height))
            throw new ArgumentException($"Loupe {window} does not fit the {settings.Width}×{settings.Height} frame.", nameof(request));

        string directory = Path.GetDirectoryName(Path.GetFullPath(request.OutPath))!;
        string baseName = Path.GetFileNameWithoutExtension(request.OutPath);
        Directory.CreateDirectory(directory);
        string Sibling(string suffix) => Path.Combine(directory, baseName + suffix);

        RenderInfo? renderInfo = null;
        ExposureInfo? exposure = null;
        DenoiseInfo? denoised = null;
        if (renderer is null)
        {
            (byte[] map, int size) = DebugImages.HeightMap(terrain, camera);
            PngEncoder.Write(Sibling(".heightmap.png"), map, size, size, PngColor.Rgb);
            outputs["heightMap"] = baseName + ".heightmap.png";
            timings["write"] = Lap(stopwatch);
        }
        else
        {
            var scene = new VistaScene(recipe, request.Seed, camera, terrain, mesh, almanac.Sun, materials, air, groundAltitude);
            var planner = new BatchPlanner(window.Width, window.Height, settings.SamplesPerPixel, settings.Batching);
            var aovs = new HashSet<Aov>(AovsFor(request.Debug));
            if (request.Denoise is not null)
                aovs.UnionWith([Aov.Albedo, Aov.Normal]);
            RenderOutput output = renderer.Render(scene, settings, aovs, planner);
            if (output.Width != window.Width || output.Height != window.Height)
                throw new InvalidOperationException($"Renderer returned {output.Width}×{output.Height}, expected {window.Width}×{window.Height}.");
            if (output.Xyz.Length != window.Pixels * 3)
                throw new InvalidOperationException($"Renderer returned {output.Xyz.Length} XYZ floats, expected {window.Pixels * 3}.");
            timings["render"] = Lap(stopwatch);
            renderInfo = RenderInfo.From(output.Stats, settings.Batching.DispatchBudgetMs);

            // The exposure meter: the whole frame at a fixed size, so every size and loupe of this view is exposed alike.
            RenderSettings meter = AutoExposure.MeterSettings(settings);
            RenderOutput metered = renderer.Render(scene, meter, new HashSet<Aov>(), new BatchPlanner(meter.Width, meter.Height, meter.SamplesPerPixel, meter.Batching));
            exposure = AutoExposure.Measure(metered.Xyz, camera.ExposureEv) with
            {
                MeterWidth = meter.Width,
                MeterHeight = meter.Height,
                MeterSamplesPerPixel = meter.SamplesPerPixel,
            };
            timings["exposureMeter"] = Lap(stopwatch);

            float[] linear = DisplayTransform.XyzToLinearRec709(output.Xyz);
            PngEncoder.Write(request.OutPath, DisplayTransform.ToSrgb8(linear, exposure.AppliedEv), window.Width, window.Height, PngColor.Rgb);
            outputs["beauty"] = Path.GetFileName(request.OutPath);
            ExrEncoder.WriteRgb(Sibling(".exr"), linear, window.Width, window.Height);
            outputs["beautyExr"] = baseName + ".exr";
            foreach (DebugOutput debug in request.Debug.OrderBy(d => d))
            {
                string role = debug.ToString().ToLowerInvariant();
                byte[] rgb = DebugImage(debug, output, materials);
                PngEncoder.Write(Sibling($".{role}.png"), rgb, window.Width, window.Height, PngColor.Rgb);
                outputs[role] = $"{baseName}.{role}.png";
            }
            timings["write"] = Lap(stopwatch);

            if (request.Denoise is { } denoise)
            {
                DenoiseInput input = Denoising.Prepare(output, linear, Denoising.InputScale(denoise, exposure), out long clamped);
                DenoiseResult result = denoiser!.Denoise(input, denoise);
                if (result.Succeeded)
                {
                    PngEncoder.Write(Sibling(".denoised.png"), DisplayTransform.ToSrgb8(result.Color, exposure.AppliedEv), window.Width, window.Height, PngColor.Rgb);
                    outputs["denoised"] = baseName + ".denoised.png";
                    ExrEncoder.WriteRgb(Sibling(".denoised.exr"), result.Color, window.Width, window.Height);
                    outputs["denoisedExr"] = baseName + ".denoised.exr";
                }
                else
                {
                    warnings.Add($"Not denoised: {result.Problem} Wrote the raw image only.");
                }
                denoised = new DenoiseInfo(denoise, result.Run, result.Succeeded ? null : result.Problem, clamped, DenoiseInfo.InputsDescription);
                timings["denoise"] = Lap(stopwatch);
            }
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
            DefaultCamera = request.CameraPath is null && request.CameraOverrides.Count == 0,
            Image = new ImageInfo(settings.Width, settings.Height, settings.SamplesPerPixel, settings.MaxBounces, DisplayTransform.Name, window)
            {
                LightSampling = ImageInfo.LightSamplingOf(settings),
                RussianRouletteStart = settings.RussianRouletteStart,
            },
            World = new WorldInfo(
                TerrainProfile.For(recipe.SetPieces).Name,
                terrain.Size,
                terrain.CellSize,
                terrain.ContentHash().ToString("x16", CultureInfo.InvariantCulture),
                terrain.MinHeight,
                terrain.MaxHeight,
                mesh.TriangleCount,
                mesh.ByteSize),
            Place = almanac.Place,
            Sun = SunInfo.From(almanac, frame),
            Atmosphere = AtmosphereInfo.From(frame, settings.Atmosphere, altitudeFrom),
            Exposure = exposure,
            Denoise = denoised,
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
