using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.Cli;

/// <summary>
/// The headless preview as a session that keeps its stages (vista-plan §7.4, §10 M1.8): load the recipe and camera, build the
/// world, mesh it, hand the scene to an <see cref="IVistaRenderer"/>, meter the exposure, and write the beauty (PNG + EXR), the
/// requested debug images, the denoised beauty and the sidecar. Each render rebuilds only the stages its inputs changed and
/// reuses the rest from the session's last render; the sidecar's <see cref="Sidecar.Stages"/> says which were which. A watch
/// session (<see cref="PreviewWatch"/>) and a contact sheet's cells run through one session; a one-shot preview
/// (<see cref="PreviewPipeline.Run"/>) through a fresh one, so everything is built.
///
/// <para>What each stage is reused by: the world and its mesh by <see cref="WorldCache"/> (the set pieces' terrain profile, the seed,
/// the grid and the erosion); the device, the acceleration structure, the atmosphere LUTs and the sky distribution by the renderer
/// itself, which reports what it rebuilt (<see cref="RenderStageKeys"/>); the render and the exposure meter here, when everything
/// the renderer reads is the same (<see cref="RenderKey"/>: the camera's exposure is display only, so changing it re-exposes the
/// image without rendering); the denoised image when the render and the denoiser's settings and input scale are. Writing the files
/// always runs, so every file on disk belongs to the last sidecar. Each reused result is exactly what a fresh session would make,
/// because every stage is deterministic (vista-plan §9.5): a watch render is bit-identical to a cold one.</para>
///
/// <para>With no renderer (<c>--dry-run</c>) a render stops before the GPU and writes the sidecar plus a top-down height map (and
/// the erosion maps when the world has them), so the world side can be inspected from any machine. A loupe request renders only its
/// window of the frame; the files are window-sized and the sidecar records the window. Since M1.3 the scene sits in a spectral
/// atmosphere at its base altitude, the EXR is in physical units and the PNG is metered (<see cref="AutoExposure"/>); since M1.6 a
/// request may also denoise; since M1.7 the world is eroded (<see cref="WorldBuilder"/>).</para>
///
/// <para>Not thread-safe: one render at a time.</para>
/// </summary>
public sealed class PreviewSession
{
    private readonly IVistaRenderer? _renderer;
    private readonly IDenoiser? _denoiser;
    private readonly ITerrainEroder? _eroder;
    private readonly ErosionCache? _erosionCache;
    private readonly TimeProvider _time;
    private readonly WorldCache _worlds = new();

    // The last image render, the last meter render and the last denoised image, with what each was made from. Set only once a
    // stage has finished, so a render that fails leaves the session as it was.
    private RenderKey? _renderKey;
    private RenderOutput? _render;
    private RenderKey? _meterKey;
    private RenderOutput? _meter;
    private DenoiseKey? _denoiseKey;
    private (DenoiseResult Result, long Clamped)? _denoised;

    /// <summary>
    /// A session rendering on <paramref name="renderer"/> (null for dry runs only), denoising on <paramref name="denoiser"/> (needed
    /// by requests that denoise), and eroding worlds on <paramref name="eroder"/> through <paramref name="erosionCache"/>.
    /// </summary>
    public PreviewSession(IVistaRenderer? renderer, IDenoiser? denoiser = null, ITerrainEroder? eroder = null, ErosionCache? erosionCache = null,
        TimeProvider? time = null)
    {
        _renderer = renderer;
        _denoiser = denoiser;
        _eroder = eroder;
        _erosionCache = erosionCache;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>True when the session can render (it has a renderer); false for a dry-run session.</summary>
    public bool CanRender => _renderer is not null;

    /// <summary>
    /// Runs <paramref name="request"/>, reusing every stage the session's last render left whose inputs are the same; returns the
    /// sidecar that was written. A watch session passes <paramref name="watch"/>, which the sidecar records with the run's wall time.
    /// </summary>
    public Sidecar Run(PreviewRequest request, WatchInfo? watch = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_renderer is null && !request.DryRun)
            throw new InvalidOperationException("A renderer is required unless the request is a dry run.");
        if (_renderer is not null && !request.DryRun && request.Denoise is not null && _denoiser is null)
            throw new InvalidOperationException("A denoiser is required when the request denoises.");

        var timings = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var outputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var stages = new SortedDictionary<PreviewStage, bool>();
        var total = Stopwatch.StartNew();
        var stopwatch = Stopwatch.StartNew();

        Recipe recipe = ReadInput(request.RecipePath, text => VistaJson.ParseRecipe(JsonOverrides.Apply(text, request.RecipeOverrides)));
        TerrainWorld world = _worlds.World(recipe, request.Seed, request.Grid, _eroder, _erosionCache, request.Erosion, out bool worldReused);
        stages[PreviewStage.World] = worldReused;
        warnings.AddRange(world.Warnings);
        HeightField terrain = world.Surface;
        timings["worldGen"] = Lap(stopwatch);

        CameraSpec camera = request.CameraPath is null
            ? DefaultCamera.For(terrain)
            : ReadInput(request.CameraPath, VistaJson.ParseCamera);
        if (request.CameraOverrides.Count > 0)
            camera = VistaJson.ParseCamera(JsonOverrides.Apply(VistaJson.Serialize(camera), request.CameraOverrides));
        if (DefaultCamera.GroundWarning(camera, terrain) is { } groundWarning)
            warnings.Add(groundWarning);

        Almanac almanac = Almanac.For(recipe);
        warnings.AddRange(almanac.Hints);
        IReadOnlyList<VistaMaterial> materials = MaterialCatalog.M0;
        AtmosphereModel air = AtmosphereModel.Default;
        (double groundAltitude, string altitudeFrom) = Places.BaseAltitude(recipe, almanac.Place);

        // The mesh follows the camera's position and the frame (vista-plan §10 M1.9), never the loupe: a loupe is a crop of the
        // frame's own render.
        var meshView = MeshView.For(camera, request.Width, request.Height, air.PlanetRadiusMetres + groundAltitude);
        TerrainMesh mesh = _worlds.Mesh(meshView, request.Mesh, out bool meshReused);
        stages[PreviewStage.Mesh] = meshReused;
        timings["mesh"] = Lap(stopwatch);
        AtmosphereFrame frame = VistaScene.FrameFor(air, groundAltitude, almanac.Sun, camera, mesh);
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
        if (_renderer is null || request.DryRun)
        {
            (byte[] map, int size) = DebugImages.HeightMap(terrain, camera);
            PngEncoder.Write(Sibling(".heightmap.png"), map, size, size, PngColor.Rgb);
            outputs["heightMap"] = baseName + ".heightmap.png";
            if (world.Erosion is { } erosion)
            {
                (byte[] maps, int mapSize) = DebugImages.ErosionMap(erosion, camera);
                PngEncoder.Write(Sibling(".erosion.png"), maps, mapSize, mapSize, PngColor.Rgb);
                outputs["erosionMap"] = baseName + ".erosion.png";
            }
            timings["write"] = Lap(stopwatch);
        }
        else
        {
            var scene = new VistaScene(recipe, request.Seed, camera, terrain, mesh, almanac.Sun, materials, air, groundAltitude);
            var aovs = new HashSet<Aov>(PreviewPipeline.AovsFor(request.Debug));
            if (request.Denoise is not null)
                aovs.UnionWith([Aov.Albedo, Aov.Normal]);
            var built = new HashSet<string>(StringComparer.Ordinal);
            bool rendered = false;

            // The image: reused when the renderer would read the same, and the last render made every output asked for now
            // (asking for fewer debug outputs never changes the beauty, vista-plan §10 M0).
            var renderKey = RenderKey.Of(scene, settings);
            RenderOutput output;
            if (_render is not null && renderKey == _renderKey && aovs.IsSubsetOf(_render.Aovs.Keys))
            {
                output = _render;
                stages[PreviewStage.Render] = true;
            }
            else
            {
                (_render, _renderKey) = (null, null);
                output = _renderer.Render(scene, settings, aovs, new BatchPlanner(window.Width, window.Height, settings.SamplesPerPixel, settings.Batching));
                if (output.Width != window.Width || output.Height != window.Height)
                    throw new InvalidOperationException($"Renderer returned {output.Width}×{output.Height}, expected {window.Width}×{window.Height}.");
                if (output.Xyz.Length != window.Pixels * 3)
                    throw new InvalidOperationException($"Renderer returned {output.Xyz.Length} XYZ floats, expected {window.Pixels * 3}.");
                (_render, _renderKey) = (output, renderKey);
                built.UnionWith(output.Stats.StageSeconds.Keys);
                rendered = true;
                stages[PreviewStage.Render] = false;
            }
            timings["render"] = Lap(stopwatch);
            renderInfo = RenderInfo.From(output.Stats, settings.Batching.DispatchBudgetMs);

            // The exposure meter: the whole frame at a fixed size, so every size and loupe of this view is exposed alike. It reads
            // neither the size nor the loupe, so it is reused across them; the camera's compensation is applied after it.
            RenderSettings meter = AutoExposure.MeterSettings(settings);
            var meterKey = RenderKey.Of(scene, meter);
            RenderOutput metered;
            if (_meter is not null && meterKey == _meterKey)
            {
                metered = _meter;
                stages[PreviewStage.ExposureMeter] = true;
            }
            else
            {
                (_meter, _meterKey) = (null, null);
                metered = _renderer.Render(scene, meter, new HashSet<Aov>(), new BatchPlanner(meter.Width, meter.Height, meter.SamplesPerPixel, meter.Batching));
                (_meter, _meterKey) = (metered, meterKey);
                built.UnionWith(metered.Stats.StageSeconds.Keys);
                rendered = true;
                stages[PreviewStage.ExposureMeter] = false;
            }
            exposure = AutoExposure.Measure(metered.Xyz, camera.ExposureEv) with
            {
                MeterWidth = meter.Width,
                MeterHeight = meter.Height,
                MeterSamplesPerPixel = meter.SamplesPerPixel,
            };
            timings["exposureMeter"] = Lap(stopwatch);
            MarkRendererStages(stages, rendered, built, settings);

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
                float scale = Denoising.InputScale(denoise, exposure);
                var denoiseKey = new DenoiseKey(renderKey, denoise, BitConverter.SingleToInt32Bits(scale));
                DenoiseResult result;
                long clamped;
                if (_denoised is { } kept && denoiseKey == _denoiseKey)
                {
                    (result, clamped) = kept;
                    stages[PreviewStage.Denoise] = true;
                }
                else
                {
                    (_denoised, _denoiseKey) = (null, null);
                    DenoiseInput input = Denoising.Prepare(output, linear, scale, out clamped);
                    result = _denoiser!.Denoise(input, denoise);
                    if (result.Succeeded)
                        (_denoised, _denoiseKey) = ((result, clamped), denoiseKey);
                    stages[PreviewStage.Denoise] = false;
                }
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
            Kind = request.DryRun || _renderer is null ? "dryRun" : "preview",
            CreatedUtc = _time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Engine = EngineInfo.Current(request.DryRun ? null : _renderer?.DeviceName),
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
                mesh.ByteSize)
            {
                Erosion = world.Info,
                Mesh = mesh.Info,
            },
            Place = almanac.Place,
            Sun = SunInfo.From(almanac, frame),
            Atmosphere = AtmosphereInfo.From(frame, settings.Atmosphere, altitudeFrom),
            Exposure = exposure,
            Denoise = denoised,
            DataTiers = MaterialCatalog.TiersOf(materials),
            Render = renderInfo,
            Stages = new StageInfo([.. stages.Where(s => !s.Value).Select(s => s.Key)], [.. stages.Where(s => s.Value).Select(s => s.Key)]),
            Watch = watch is null ? null : watch with { RunSeconds = total.Elapsed.TotalSeconds },
            TimingsSeconds = timings,
            Outputs = outputs,
            Warnings = warnings,
        };
        File.WriteAllText(Sibling(".json"), VistaJson.Serialize(sidecar));
        return sidecar;
    }

    /// <summary>
    /// The renderer's own stages (<see cref="RenderStageKeys"/>): built when a render that ran reported building them, reused
    /// otherwise; all reused when nothing was rendered. The sky distribution exists only when the sky is sampled as a light, which
    /// the exposure meter (always through the LUTs) does whenever the image's light sampling is MIS.
    /// </summary>
    private static void MarkRendererStages(SortedDictionary<PreviewStage, bool> stages, bool rendered, HashSet<string> built, RenderSettings settings)
    {
        void Mark(PreviewStage stage, params string[] keys) => stages[stage] = !rendered || !keys.Any(built.Contains);
        Mark(PreviewStage.Device, RenderStageKeys.DeviceInit, RenderStageKeys.ShaderCompile, RenderStageKeys.ReferenceShaderCompile);
        Mark(PreviewStage.AccelerationStructure, RenderStageKeys.MeshUpload, RenderStageKeys.AccelerationBuild);
        Mark(PreviewStage.AtmosphereTables, RenderStageKeys.AtmosphereTables);
        Mark(PreviewStage.ScatteringOrders, RenderStageKeys.ScatteringOrders);
        Mark(PreviewStage.SkyView, RenderStageKeys.SkyView);
        if (settings.LightSampling == LightSampling.Mis)
            Mark(PreviewStage.SkyDistribution, RenderStageKeys.SkyDistribution);
    }

    /// <summary>Reads and parses an input file; an error in it names the file, since a watch reads several that may have changed.</summary>
    private static T ReadInput<T>(string path, Func<string, T> parse)
    {
        string text = File.ReadAllText(path);
        try
        {
            return parse(text);
        }
        catch (JsonException e)
        {
            throw new JsonException($"{path}: {e.Message}", e.Path, e.LineNumber, e.BytePositionInLine, e);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
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

    /// <summary>
    /// Everything a renderer reads of one render: the scene but for its recipe and seed (the renderer reads them only through the
    /// world, the sun, the materials and the base altitude, which are here) and the camera's exposure (display only, so it is
    /// zeroed), and the settings. Worlds and meshes compare by instance: the session's <see cref="WorldCache"/> hands out the same
    /// one while it holds. Lists compare by instance too, so a scene built afresh can only cost a render, never reuse a wrong one.
    /// A field added to <see cref="VistaScene"/> must join this key (a test enumerates them).
    /// </summary>
    internal sealed record RenderKey(
        TerrainMesh Mesh,
        HeightField Terrain,
        CameraSpec Camera,
        SunPosition Sun,
        IReadOnlyList<VistaMaterial> Materials,
        AtmosphereModel Air,
        double GroundAltitudeMetres,
        RenderSettings Settings)
    {
        /// <summary>The <see cref="VistaScene"/> fields the key leaves out, because the renderer does not read them.</summary>
        internal static IReadOnlySet<string> SceneFieldsNotRead { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(VistaScene.Recipe), nameof(VistaScene.Seed),
        };

        /// <summary>The key of rendering <paramref name="scene"/> with <paramref name="settings"/>.</summary>
        public static RenderKey Of(VistaScene scene, RenderSettings settings) => new(
            scene.Mesh, scene.Terrain, scene.Camera with { ExposureEv = 0.0 }, scene.Sun, scene.Materials, scene.Air, scene.GroundAltitudeMetres, settings);
    }

    /// <summary>What a denoised image is made from: the render, the settings and the input scale (its bits; NaN lets the filter choose).</summary>
    private sealed record DenoiseKey(RenderKey Render, DenoiseSettings Settings, int InputScaleBits);
}
