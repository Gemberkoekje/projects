using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Composition;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.Sliders;
using Vista.Weather;
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
/// itself, which reports what it rebuilt (<see cref="RenderStageKeys"/>); the render here, when everything the renderer reads is the
/// same (<see cref="RenderKey"/>: the camera's exposure is display only, so changing it re-exposes the image without rendering); the
/// exposure meter, as the incident meter when the air's frame is the same, as the average meter when its render's key is; the
/// denoised image when the render and the denoiser's settings and input scale are. Writing the files always runs, so every file on
/// disk belongs to the last sidecar, and the meter and the display curve (M1.10a) only re-expose and re-encode a kept render. Each
/// reused result is exactly what a fresh session would make, because every stage is deterministic (vista-plan §9.5): a watch render
/// is bit-identical to a cold one.</para>
///
/// <para>With no renderer (<c>--dry-run</c>) a render stops before the GPU and writes the sidecar plus a top-down height map (and
/// the erosion maps, the drainage and the rock's hardness when the world has them), so the world side can be inspected from any machine. A loupe request renders only its
/// window of the frame; the files are window-sized and the sidecar records the window. Since M1.3 the scene sits in a spectral
/// atmosphere at its base altitude, the EXR is in physical units and the PNG is metered (<see cref="AutoExposure"/>); since M1.6 a
/// request may also denoise; since M1.7 the world is eroded (<see cref="WorldBuilder"/>); since M1.10a the PNG is metered by the
/// incident meter (<see cref="IncidentMeter"/>) and display-encoded through AgX unless the request asks otherwise; since M1.11c it
/// is dithered before its 8-bit rounding, or written at 16 bits.</para>
///
/// <para>Since M1.15 a request may ask for an estimate instead (<c>--estimate</c>, <c>PreviewSession.Estimate.cs</c>), and every image
/// render plans its video memory first (refused when it does not fit the GPU's budget, warned about when it is tight) and runs under a
/// <see cref="TraceClock"/>, whose live ETA goes to <see cref="Progress"/> and whose checkpoints and the memory's plan and peak go in the
/// sidecar.</para>
///
/// <para>Not thread-safe: one render at a time.</para>
/// </summary>
public sealed partial class PreviewSession
{
    private readonly IVistaRenderer? _renderer;
    private readonly IDenoiser? _denoiser;
    private readonly ITerrainEroder? _eroder;
    private readonly ErosionCache? _erosionCache;
    private readonly TimeProvider _time;
    private readonly WorldCache _worlds = new();

    // The last image render, the last meter render, the last incident measurement and the last denoised image, with what each was
    // made from. Set only once a stage has finished, so a render that fails leaves the session as it was.
    private RenderKey? _renderKey;
    private RenderOutput? _render;
    private RenderKey? _meterKey;
    private RenderOutput? _meter;
    private (AtmosphereFrame Frame, Quadrature Quadrature)? _incidentKey;
    private IncidentLight? _incident;
    private DenoiseKey? _denoiseKey;
    private (DenoiseResult Result, long Clamped)? _denoised;

    // The spectral tables the incident meter weighs its card's light with, for the materials and the wavelengths they were built on.
    private (IReadOnlyList<VistaMaterial> Materials, WavelengthTable Wavelengths, IntegratorTables Tables)? _tables;

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
    /// The seconds from the process's start to the session's first run, before the renderer was made (vista-plan §10 M1.15): an estimate
    /// counts them, since a final run repeats them. 0 when unknown.
    /// </summary>
    public double StartupSeconds { get; init; }

    /// <summary>Told at once what the sidecar's warnings will say later, when it matters before a render starts (a tight video memory).</summary>
    public Action<string>? Notice { get; init; }

    /// <summary>Told the image render's progress and its live ETA after every batch (vista-plan §7.2, §10 M1.15).</summary>
    public Action<TraceProgress>? Progress { get; init; }

    /// <summary>
    /// The image the last run rendered or reused (null before one, and after a run that failed or rendered nothing), for tools that
    /// compare two renders by their colour (vista-plan §10 M1.12a's <c>--quadrature-compare</c>).
    /// </summary>
    public RenderOutput? LastRender => _render;

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

        // The auto camera (M1.14) chooses its camera below, once the sun, the pools and the ground are known.
        CameraSpec camera = request.CameraPath is null
            ? DefaultCamera.For(terrain)
            : ReadInput(request.CameraPath, VistaJson.ParseCamera);
        if (request.CameraOverrides.Count > 0 && request.AutoCamera is null)
            camera = VistaJson.ParseCamera(JsonOverrides.Apply(VistaJson.Serialize(camera), request.CameraOverrides));
        if (request.AutoCamera is null && DefaultCamera.GroundWarning(camera, terrain) is { } groundWarning)
            warnings.Add(groundWarning);

        // The linked sliders (vista-plan §10 M1.11): the pins, resolved with the set pieces into the place, the sun and the weather.
        (SliderPins pins, string pinsFrom) = ReadPins(recipe, request);
        SliderState sliders = LinkedSliders.Resolve(recipe, pins, AtmosphereModel.Default, world.Footing);
        warnings.AddRange(sliders.Hints);
        Almanac almanac = sliders.Almanac;
        IReadOnlyList<VistaMaterial> materials = MaterialCatalog.For(request.Mesh.Materials);
        double groundAltitude = sliders.BaseAltitudeMetres;
        // The valleys' pools (vista-plan §10 M1.13b): each valley's from the night's pool and its shape; the air's tables carry their
        // haze as its altitude average, and the march takes them in three dimensions.
        ValleyPools pools = sliders.Pools.Any && request.Pools ? ValleyPools.Build(_worlds.Pools(), sliders.Pools, groundAltitude) : ValleyPools.None;
        AtmosphereModel air = request.Pools ? sliders.Air(AtmosphereModel.Default, LowAir.TablesLayer(pools, request.LowAir)) : sliders.Air(AtmosphereModel.Default);
        LowAir? lowAir = LowAir.For(pools, air, request.Seed, request.LowAir);
        timings["pools"] = Lap(stopwatch);
        // The season's snow from the sliders, and the world's glaciers' line at this altitude (vista-plan §10 M1.13).
        GroundCover ground = GroundCover.For(sliders.Climate, almanac.Place.LatitudeDegrees) with
        {
            Forest = request.Forest,
            Snow = request.Snow,
            SnowlineMetres = sliders.SnowlineMetres,
            FreezingLevelMetres = sliders.FreezingLevelMetres,
            GlacierLineMetres = world.Info.Settings.GlacierElaMetres + groundAltitude,
        };
        string altitudeFrom = sliders.Readings[Slider.Altitude].From;

        string directory = Path.GetDirectoryName(Path.GetFullPath(request.OutPath))!;
        string baseName = Path.GetFileNameWithoutExtension(request.OutPath);
        Directory.CreateDirectory(directory);
        string Sibling(string suffix) => Path.Combine(directory, baseName + suffix);

        // The auto camera (vista-plan §10 M1.14): candidates around the set piece's planted shot, scored from their composition buffers,
        // the best finalists rendered small; the pick is written beside the preview, and the camera options apply on top of it.
        AutoCameraInfo? autoCamera = null;
        if (request.AutoCamera is { } auto)
        {
            (camera, autoCamera) = ChooseCamera(request, auto, recipe, world, sliders, ground, groundAltitude, air, pools, lowAir, materials, warnings);
            File.WriteAllText(Sibling(".camera.json"), VistaJson.Serialize(camera));
            outputs["autoCamera"] = baseName + ".camera.json";
            if (request.CameraOverrides.Count > 0)
                camera = VistaJson.ParseCamera(JsonOverrides.Apply(VistaJson.Serialize(camera), request.CameraOverrides));
            timings["autoCamera"] = Lap(stopwatch);
        }

        // The mesh follows the camera's position and the frame (vista-plan §10 M1.9), never the loupe: a loupe is a crop of the
        // frame's own render; and the forest's crowns follow the place's tree line and the altitude of the world (M1.12b).
        var meshView = MeshView.For(camera, request.Width, request.Height, air.PlanetRadiusMetres + groundAltitude);
        TerrainMesh mesh = _worlds.Mesh(meshView, request.Mesh with { Forest = ForestOf(ground, groundAltitude) }, out bool meshReused);
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
            Diffuse = request.Diffuse,
            Canopy = request.Canopy,
        };
        PixelWindow window = settings.Rendered;
        if (!window.FitsIn(settings.Width, settings.Height))
            throw new ArgumentException($"Loupe {window} does not fit the {settings.Width}×{settings.Height} frame.", nameof(request));

        RenderInfo? renderInfo = null;
        ExposureInfo? exposure = null;
        MaterialShares? shares = null;
        IReadOnlyList<SlopeBand>? snowBySlope = null;
        DenoiseInfo? denoised = null;
        DetailInfo? detail = null;
        EstimateInfo? estimate = null;
        VistaScene SceneWith(CameraSpec view, TerrainMesh viewMesh) =>
            new(recipe, request.Seed, view, terrain, viewMesh, almanac.Sun, materials, air, groundAltitude)
            {
                Ground = ground,
                Quadrature = request.Quadrature,
                LowAir = lowAir,
            };
        if (request.Estimate && _renderer is not null && !request.DryRun)
        {
            // The estimate (vista-plan §10 M1.15) instead of the image: PreviewSession.Estimate.cs.
            var view = new EstimateView(SceneWith(camera, mesh), settings, frame, world.Surface, air.PlanetRadiusMetres + groundAltitude,
                ForestOf(ground, groundAltitude));
            (estimate, exposure) = Estimate(request, view, SceneWith, stages, timings, warnings, Sibling);
        }
        else if (_renderer is null || request.DryRun)
        {
            (byte[] map, int size) = DebugImages.HeightMap(terrain, camera);
            PngEncoder.Write(Sibling(".heightmap.png"), map, size, size, PngColor.Rgb);
            outputs["heightMap"] = baseName + ".heightmap.png";
            if (world.Erosion is { } erosion)
            {
                (byte[] maps, int mapSize) = DebugImages.ErosionMap(erosion, camera);
                PngEncoder.Write(Sibling(".erosion.png"), maps, mapSize, mapSize, PngColor.Rgb);
                outputs["erosionMap"] = baseName + ".erosion.png";
                (byte[] drainage, int drainageSize) = DebugImages.DrainageImage(erosion.Surface, erosion.Drainage, camera);
                PngEncoder.Write(Sibling(".drainage.png"), drainage, drainageSize, drainageSize, PngColor.Rgb);
                outputs["drainageMap"] = baseName + ".drainage.png";
                (byte[] hardness, int hardnessSize) = DebugImages.HardnessImage(erosion, camera);
                PngEncoder.Write(Sibling(".hardness.png"), hardness, hardnessSize, hardnessSize, PngColor.Rgb);
                outputs["hardnessMap"] = baseName + ".hardness.png";
            }
            if (pools.Any)
            {
                // The valleys' pools at the hour (M1.13b): their haze and mist over the ground.
                (byte[] lying, int lyingSize) = DebugImages.PoolImage(terrain, pools, groundAltitude, camera);
                PngEncoder.Write(Sibling(".pools.png"), lying, lyingSize, lyingSize, PngColor.Rgb);
                outputs["poolMap"] = baseName + ".pools.png";
            }
            if (mesh.Materials is { } field)
            {
                // The ground's materials from above (M1.12): every sample of the world grid under this place's grass line.
                MaterialWeights[] weights = field.SampleWeights(terrain, MaterialConstantsOf(field, ground), (float)groundAltitude);
                (byte[] cover, int coverSize) = DebugImages.MaterialMap(terrain, field, weights, camera);
                PngEncoder.Write(Sibling(".materials.png"), cover, coverSize, coverSize, PngColor.Rgb);
                outputs["materialMap"] = baseName + ".materials.png";
                shares = SharesOf(field, terrain, weights);
            }
            timings["write"] = Lap(stopwatch);
        }
        else
        {
            VistaScene scene = SceneWith(camera, mesh);
            HashSet<Aov> aovs = AovsOf(request);
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
                renderInfo = RenderInfo.From(output.Stats, settings.Batching.DispatchBudgetMs);
            }
            else
            {
                (_render, _renderKey) = (null, null);
                // Its video memory planned before anything is allocated (M1.15): refused when it does not fit, warned about when tight.
                MemoryPlan plan = CheckMemory(scene, settings, aovs, warnings);
                var planner = new BatchPlanner(window.Width, window.Height, settings.SamplesPerPixel, settings.Batching);
                var clock = new TraceClock(planner, _time);
                if (Progress is { } progress)
                    clock.Progressed += progress;
                output = _renderer.Render(scene, settings, aovs, planner);
                if (output.Width != window.Width || output.Height != window.Height)
                    throw new InvalidOperationException($"Renderer returned {output.Width}×{output.Height}, expected {window.Width}×{window.Height}.");
                if (output.Xyz.Length != window.Pixels * 3)
                    throw new InvalidOperationException($"Renderer returned {output.Xyz.Length} XYZ floats, expected {window.Pixels * 3}.");
                (_render, _renderKey) = (output, renderKey);
                built.UnionWith(output.Stats.StageSeconds.Keys);
                rendered = true;
                stages[PreviewStage.Render] = false;
                renderInfo = RenderInfo.From(output.Stats, settings.Batching.DispatchBudgetMs) with
                {
                    Memory = MemoryInfo.Of(plan, output.Stats.PeakVideoMemoryBytes),
                    Trace = TraceInfo.Of(clock),
                };
            }
            timings["render"] = Lap(stopwatch);

            exposure = MeterExposure(request, scene, settings, frame, materials, stages, built, ref rendered);
            timings["exposureMeter"] = Lap(stopwatch);
            MarkRendererStages(stages, rendered, built, settings);

            if (output.Aovs.TryGetValue(Aov.MaterialId, out float[]? ids) && output.Aovs.TryGetValue(Aov.Normal, out float[]? normals) && mesh.Materials is not null)
                snowBySlope = SnowBySlope.Measure(ids, normals);

            float[] linear = DisplayTransform.XyzToLinearRec709(output.Xyz);
            if (output.Aovs.TryGetValue(Aov.Variance, out float[]? variance))
            {
                // The noise the image shows (M1.15), from its own variance: what an estimate's preview predicted it would.
                float[] steps = NoiseMeter.OneSampleSteps(linear, output.Xyz, variance, settings.SamplesPerPixel, exposure.AppliedEv, request.Display, request.Look);
                renderInfo = renderInfo with
                {
                    NoiseSteps = NoiseMeter.StepsAt(NoiseMeter.PercentileOf(steps, NoiseMeter.Percentile), settings.SamplesPerPixel),
                };
            }
            detail = WriteImages(request, output, linear, exposure, window, camera, almanac.Sun, world.Surface, air.PlanetRadiusMetres + groundAltitude,
                request.OutPath, Sibling, baseName, outputs, warnings, out _);
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
                    WriteDisplayPng(Sibling(".denoised.png"), result.Color, exposure.AppliedEv, request, window);
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

        // An estimate's sidecar sits beside the final's own, never in its place.
        string sidecarSuffix = estimate is null ? ".json" : ".estimate.json";
        outputs["sidecar"] = baseName + sidecarSuffix;
        string kind = estimate is null ? "preview" : "estimate";
        var sidecar = new Sidecar
        {
            Kind = request.DryRun || _renderer is null ? "dryRun" : kind,
            CreatedUtc = _time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            Engine = EngineInfo.Current(request.DryRun ? null : _renderer?.DeviceName),
            Recipe = recipe,
            Seed = request.Seed,
            Camera = camera,
            DefaultCamera = request.CameraPath is null && request.CameraOverrides.Count == 0 && request.AutoCamera is null,
            AutoCamera = autoCamera,
            Detail = detail,
            Image = new ImageInfo(settings.Width, settings.Height, settings.SamplesPerPixel, settings.MaxBounces, DisplayTransform.NameOf(request.Display, request.Look), window)
            {
                LightSampling = ImageInfo.LightSamplingOf(settings),
                RussianRouletteStart = settings.RussianRouletteStart,
                Diffuse = settings.Diffuse,
                Canopy = settings.Canopy,
                Quadrature = request.Quadrature,
                Wavelengths = WavelengthsInfo.Of(WavelengthTable.For(request.Quadrature)),
                Display = request.Display,
                Look = DisplayTransform.LookOf(request.Display, request.Look),
                Dither = DisplayTransform.DitherOf(request.BitDepth, request.Dither),
                BitDepth = request.BitDepth,
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
                Footing = world.Footing,
                Glaciers = GlacierInfo.Of(world.Erosion, world.Info.Settings),
            },
            Place = almanac.Place,
            Sliders = SlidersInfo.From(sliders, request.SlidersPath, pinsFrom).WithPools(PoolsInfo.Of(request.Pools, sliders.Pools, pools, air)),
            Sun = SunInfo.From(almanac, frame),
            Atmosphere = AtmosphereInfo.From(frame, settings.Atmosphere, altitudeFrom),
            Exposure = exposure,
            Denoise = denoised,
            DataTiers = MaterialCatalog.TiersOf(materials),
            Materials = MaterialsInfoOf(Tables(materials, WavelengthTable.For(request.Quadrature)), materials, mesh, ground, sliders.Climate, shares) is { } info
                ? info with { SnowBySlope = snowBySlope }
                : null,
            Render = renderInfo,
            Estimate = estimate,
            Stages = new StageInfo([.. stages.Where(s => !s.Value).Select(s => s.Key)], [.. stages.Where(s => s.Value).Select(s => s.Key)]),
            Watch = watch is null ? null : watch with { RunSeconds = total.Elapsed.TotalSeconds },
            TimingsSeconds = timings,
            Outputs = outputs,
            Warnings = warnings,
        };
        File.WriteAllText(Sibling(sidecarSuffix), VistaJson.Serialize(sidecar));
        return sidecar;
    }

    /// <summary>The renderer outputs <paramref name="request"/>'s image needs: its debug images' and, when it denoises, the albedo and the normal.</summary>
    private static HashSet<Aov> AovsOf(PreviewRequest request)
    {
        var aovs = new HashSet<Aov>(PreviewPipeline.AovsFor(request.Debug));
        if (request.Denoise is not null)
            aovs.UnionWith([Aov.Albedo, Aov.Normal]);
        return aovs;
    }

    /// <summary>
    /// The video memory of an image render, planned before it (vista-plan §10 M1.15): a render that does not fit the GPU's budget is
    /// refused with the plan's numbers; a tight one is warned about at once (<see cref="Notice"/>) and in the sidecar.
    /// </summary>
    private MemoryPlan CheckMemory(VistaScene scene, RenderSettings settings, IReadOnlySet<Aov> aovs, List<string> warnings)
    {
        MemoryPlan plan = _renderer!.PlanMemory(scene, settings, aovs);
        if (plan.Warning() is { } warning)
        {
            if (!plan.Fits)
                throw new InsufficientMemoryException(warning);
            warnings.Add(warning);
            Notice?.Invoke(warning);
        }
        return plan;
    }

    /// <summary>
    /// The PNG's exposure for <paramref name="scene"/> (vista-plan §10 M1.3, M1.10a, M1.11b), metering it unless the session's last meter
    /// reading is for the same view. The exposure meter reads neither the size nor the loupe, so every size and loupe of this view is
    /// exposed alike and the meter is reused across them; the camera's compensation and the meter's own (M1.11b) are applied after it. The
    /// incident meter (M1.10a) reads the air's LUTs the render just built: the light on a grey card where the camera stands. The average
    /// meter renders the whole frame at a fixed size.
    /// </summary>
    private ExposureInfo MeterExposure(PreviewRequest request, VistaScene scene, RenderSettings settings, AtmosphereFrame frame,
        IReadOnlyList<VistaMaterial> materials, SortedDictionary<PreviewStage, bool> stages, HashSet<string> built, ref bool rendered)
    {
        CameraSpec camera = scene.Camera;
        ExposureInfo exposure;
        double meterCompensation = request.MeterCompensationEv
            ?? (request.Meter == Metering.Incident ? IncidentMeter.DefaultCompensationEv : 0.0);
        if (request.Meter == Metering.Incident)
        {
            // Keyed by the view's air frame (the scene's own, VistaScene.AirFrame): the sun, the camera's altitude, the air; and the
            // wavelengths it is measured at (M1.12a).
            IncidentLight light;
            if (_incident is not null && _incidentKey == (frame, request.Quadrature))
            {
                light = _incident;
                stages[PreviewStage.ExposureMeter] = true;
            }
            else
            {
                (_incident, _incidentKey) = (null, null);
                light = _renderer!.MeasureIncidentLight(scene);
                (_incident, _incidentKey) = (light, (frame, request.Quadrature));
                built.UnionWith(light.StageSeconds.Keys);
                rendered = true;
                stages[PreviewStage.ExposureMeter] = false;
            }
            exposure = IncidentMeter.Measure(light, frame, Tables(materials, scene.Wavelengths()), camera.ExposureEv + meterCompensation);
        }
        else
        {
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
                metered = _renderer!.Render(scene, meter, new HashSet<Aov>(), new BatchPlanner(meter.Width, meter.Height, meter.SamplesPerPixel, meter.Batching));
                (_meter, _meterKey) = (metered, meterKey);
                built.UnionWith(metered.Stats.StageSeconds.Keys);
                rendered = true;
                stages[PreviewStage.ExposureMeter] = false;
            }
            exposure = AutoExposure.Measure(metered.Xyz, camera.ExposureEv + meterCompensation);
            exposure = exposure with
            {
                Average = exposure.Average! with { MeterWidth = meter.Width, MeterHeight = meter.Height, MeterSamplesPerPixel = meter.SamplesPerPixel },
            };
        }
        return exposure with { CompensationEv = camera.ExposureEv, MeterCompensationEv = meterCompensation };
    }

    /// <summary>
    /// Writes the beauty as PNG (<paramref name="beautyPath"/>) and EXR and the requested debug images (<paramref name="path"/> turns a
    /// suffix into a sibling's path), recording each in <paramref name="outputs"/> under <paramref name="baseName"/> when given; returns the
    /// detail budget's measure when its image was written, and in <paramref name="beautySeconds"/> how long the PNG and the EXR took.
    /// </summary>
    private DetailInfo? WriteImages(PreviewRequest request, RenderOutput output, float[] linear, ExposureInfo exposure, PixelWindow window,
        CameraSpec camera, SunPosition sun, HeightField surface, double radius, string beautyPath, Func<string, string> path, string baseName,
        SortedDictionary<string, string>? outputs, List<string> warnings, out double beautySeconds)
    {
        var stopwatch = Stopwatch.StartNew();
        WriteDisplayPng(beautyPath, linear, exposure.AppliedEv, request, window);
        ExrEncoder.WriteRgb(path(".exr"), linear, window.Width, window.Height);
        beautySeconds = stopwatch.Elapsed.TotalSeconds;
        if (outputs is not null)
        {
            outputs["beauty"] = Path.GetFileName(beautyPath);
            outputs["beautyExr"] = baseName + ".exr";
        }
        DetailInfo? detail = null;
        foreach (DebugOutput debug in request.Debug.OrderBy(d => d))
        {
            string role = debug.ToString().ToLowerInvariant();
            byte[] rgb;
            if (debug == DebugOutput.Detail)
            {
                // The auto camera's detail budget over the beauty (M1.14's follow-up); a loupe's pixels are not the frame's buffer.
                if (!request.Loupe.IsEmpty)
                {
                    warnings.Add("The detail budget's image is of the whole frame: not written for a loupe.");
                    continue;
                }
                (rgb, detail) = DetailImage(output, linear, exposure.AppliedEv, request, camera, sun, surface, radius);
            }
            else
            {
                rgb = DebugImage(debug, output, MaterialCatalog.For(request.Mesh.Materials));
            }
            PngEncoder.Write(path($".{role}.png"), rgb, window.Width, window.Height, PngColor.Rgb);
            if (outputs is not null)
                outputs[role] = $"{baseName}.{role}.png";
        }
        return detail;
    }

    /// <summary>
    /// The auto camera's pick for <paramref name="request"/> (vista-plan §10 M1.14, <see cref="Composition.AutoCamera"/>): the set piece's
    /// shot planted on the world, its candidates scored from composition buffers traced on the CPU, and the best
    /// <see cref="AutoCameraSettings.Finalists"/> rendered at <see cref="AutoCameraSettings.FinalistWidth"/> for the squint test and the
    /// values by layer (exposed by the incident meter and display-encoded as the preview is), their own buffers rescored, and their 4K
    /// meshes' triangles counted; the best finalist that keeps every limit is the pick. Without a renderer (a dry run) or with no
    /// finalists asked for, the best candidate by its buffer is.
    /// </summary>
    private (CameraSpec Camera, AutoCameraInfo Info) ChooseCamera(PreviewRequest request, AutoCameraSettings auto, Recipe recipe, TerrainWorld world,
        SliderState sliders, GroundCover ground, double groundAltitude, AtmosphereModel air, ValleyPools pools, LowAir? lowAir,
        IReadOnlyList<VistaMaterial> materials, List<string> warnings)
    {
        CompositionIntent intent = Composition.AutoCamera.IntentOf(recipe);
        if (intent == CompositionIntent.None)
            throw new InvalidDataException($"{request.RecipePath}: the auto camera frames the mountain set pieces (snowCappedMountains, alpineRange), and the recipe has neither.");
        CompositionWeights weights = CompositionWeights.Default;
        // Without the detail budget its share is measured but never breaks the limit (M1.14's search as it was).
        CompositionLimits limits = auto.DetailBudget ? CompositionLimits.Default : CompositionLimits.Default with { DetailMaxShare = 1.0 };
        var seconds = new SortedDictionary<string, double>(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();

        double radius = air.PlanetRadiusMetres + groundAltitude;
        ForestPlacement forest = ForestOf(ground, groundAltitude);
        IGroundClassifier? classes = null;
        if (request.Mesh.Materials)
        {
            MaterialField field = _worlds.Materials(forest);
            classes = new MaterialClassifier(field, MaterialConstantsOf(field, ground), (float)groundAltitude);
        }
        PoolMap floors = _worlds.Pools();
        var sight = new TerrainSight(world.Surface, request.Mesh.FarField ? _worlds.FarTerrain() : null, radius, classes, floors);
        double aspect = (double)request.Width / request.Height;
        SunPosition sun = sliders.Almanac.Sun;
        PlantedShot shot = ShotPlanter.Plant(sight, sun, intent, weights, limits, aspect);
        seconds["plant"] = Lap(stopwatch);

        IReadOnlyList<WorldPoint> shoulders = Composition.AutoCamera.Shoulders(pools, groundAltitude, sight, shot.Viewpoint, 0.5 * shot.DistanceMetres, auto.Shoulders);
        IReadOnlyList<CameraCandidate> candidates = Composition.AutoCamera.Candidates(sight, shot, aspect, auto, shoulders);
        if (candidates.Count == 0)
            throw new InvalidDataException("The auto camera found no place that sees the main peak.");
        ScoredCandidate[] ranked = Composition.AutoCamera.Rank(Composition.AutoCamera.Score(sight, candidates, sun, shot, aspect, auto, weights, limits));
        seconds["buffers"] = Lap(stopwatch);

        ScoredCandidate[] finalists = Composition.AutoCamera.Finalists(ranked, auto);
        string? note = null;
        var rendered = new List<ScoredCandidate>();
        if (_renderer is null || request.DryRun)
            note = "dry run: no finalist rendered, the best candidate by its buffer is the pick";
        else if (auto.Finalists == 0)
            note = "no finalists asked for: the best candidate by its buffer is the pick";
        else if (finalists.Length == 0)
            note = "no candidate's buffer kept the limits: none rendered";
        else
        {
            int fw = auto.FinalistWidth, fh = Math.Max(4, (int)Math.Round(fw / aspect));
            MeshSettings meshSettings = request.Mesh with { Forest = forest };
            foreach (ScoredCandidate finalist in finalists)
            {
                CameraSpec camera = finalist.Candidate.Camera;
                TerrainMesh mesh = _worlds.Mesh(MeshView.For(camera, fw, fh, radius), meshSettings);
                var scene = new VistaScene(recipe, request.Seed, camera, world.Surface, mesh, sun, materials, air, groundAltitude)
                {
                    Ground = ground,
                    Quadrature = request.Quadrature,
                    LowAir = lowAir,
                };
                var settings = new RenderSettings
                {
                    Width = fw,
                    Height = fh,
                    SamplesPerPixel = auto.FinalistSamples,
                    MaxBounces = request.MaxBounces,
                    Batching = new BatchPlannerOptions { DispatchBudgetMs = request.DispatchBudgetMs },
                    LightSampling = request.LightSampling,
                    RussianRouletteStart = request.RussianRouletteStart,
                    Diffuse = request.Diffuse,
                    Canopy = request.Canopy,
                };
                RenderOutput output = _renderer.Render(scene, settings, new HashSet<Aov> { Aov.Depth, Aov.Normal, Aov.MaterialId },
                    new BatchPlanner(fw, fh, settings.SamplesPerPixel, settings.Batching));
                IncidentLight light = _renderer.MeasureIncidentLight(scene);
                ExposureInfo exposure = IncidentMeter.Measure(light, scene.AirFrame(), Tables(materials, scene.Wavelengths()),
                    camera.ExposureEv + (request.MeterCompensationEv ?? IncidentMeter.DefaultCompensationEv));
                byte[] srgb = DisplayTransform.ToSrgb8(DisplayTransform.XyzToLinearRec709(output.Xyz), exposure.AppliedEv, request.Display, request.Look);
                var values = new float[fw * fh];
                for (int p = 0; p < values.Length; p++)
                    values[p] = (0.2126f * srgb[p * 3] + 0.7152f * srgb[p * 3 + 1] + 0.0722f * srgb[p * 3 + 2]) / 255f;
                CompositionBuffer buffer = CompositionBuffer.FromRender(output, camera, fw, fh, floors, radius);
                CompositionScores scores = CompositionScorer.Score(buffer, Composition.AutoCamera.Context(camera, sun, shot), limits, sight);
                (double squint, double separation, double spread) = ValueStructure.Squint(values, fw, fh);
                (double layered, double[] means) = ValueStructure.Layers(values, buffer);
                scores = scores with
                {
                    Squint = squint,
                    SquintSeparation = separation,
                    SquintSpread = spread,
                    Values = layered,
                    LayerValues = means.All(double.IsFinite) ? means : null,
                };
                rendered.Add(Composition.AutoCamera.Scored(finalist.Candidate, scores, weights, limits));
            }
            seconds["finalists"] = Lap(stopwatch);
            // The final frame's mesh counted against the acceleration structure's ceiling (vista-plan §10 M1.9), best first, until one
            // keeps it: a count takes seconds at 4K, and the best usually does.
            ScoredCandidate[] order = Composition.AutoCamera.Rank(rendered);
            for (int k = 0; k < order.Length; k++)
            {
                if (!order[k].Feasible)
                    continue;
                CameraSpec camera = order[k].Candidate.Camera;
                long triangles = _worlds.CountTriangles(MeshView.For(camera, limits.FinalFrameWidth, limits.FinalFrameHeight, radius), meshSettings);
                order[k] = Composition.AutoCamera.Scored(order[k].Candidate, order[k].Scores with { Triangles = triangles }, weights, limits);
                if (order[k].Feasible)
                    break;
            }
            rendered = [.. order];
            seconds["meshes"] = Lap(stopwatch);
        }

        ScoredCandidate[] final = Composition.AutoCamera.Rank(rendered);
        ScoredCandidate pick = final.Length > 0 ? final[0] : ranked[0];
        if (!pick.Feasible)
            warnings.Add($"The auto camera's pick breaks a limit ({pick.Breaks}): no candidate kept them all.");
        var info = new AutoCameraInfo
        {
            Planted = shot,
            Settings = auto,
            Weights = weights,
            Limits = limits,
            Candidates = candidates.Count,
            Feasible = ranked.Count(s => s.Feasible),
            Shoulders = shoulders.Count,
            Pick = AutoCameraPick.Of(pick),
            Finalists = [.. final.Select(AutoCameraPick.Of)],
            Best = [.. ranked.Take(10).Select(AutoCameraPick.Of)],
            Seconds = seconds,
            Note = note,
        };
        return (pick.Candidate.Camera, info);
    }

    /// <summary>
    /// The renderer's own stages (<see cref="RenderStageKeys"/>): built when a render that ran reported building them, reused
    /// otherwise; all reused when nothing was rendered. The sky distribution exists only when the sky is sampled as a light, which
    /// the exposure meter (always through the LUTs) does whenever the image's light sampling is MIS.
    /// </summary>
    private static void MarkRendererStages(SortedDictionary<PreviewStage, bool> stages, bool rendered, HashSet<string> built, RenderSettings settings)
    {
        void Mark(PreviewStage stage, params string[] keys) => stages[stage] = !rendered || !keys.Any(built.Contains);
        Mark(PreviewStage.Device, RenderStageKeys.DeviceInit, RenderStageKeys.ShaderCompile, RenderStageKeys.ReferenceShaderCompile,
            RenderStageKeys.LowAirShaderCompile);
        Mark(PreviewStage.AccelerationStructure, RenderStageKeys.MeshUpload, RenderStageKeys.AccelerationBuild);
        Mark(PreviewStage.AtmosphereTables, RenderStageKeys.AtmosphereTables);
        Mark(PreviewStage.ScatteringOrders, RenderStageKeys.ScatteringOrders);
        Mark(PreviewStage.SkyView, RenderStageKeys.SkyView);
        if (settings.LightSampling == LightSampling.Mis)
            Mark(PreviewStage.SkyDistribution, RenderStageKeys.SkyDistribution);
    }

    /// <summary>
    /// Writes the beauty or the denoised beauty, <paramref name="linear"/> Rec.709 of <paramref name="window"/>, as the PNG
    /// <paramref name="request"/> asks for (vista-plan §10 M1.11c): exposed by <paramref name="exposureEv"/>, display-encoded, then at
    /// 8 bits with its dither keyed by the frame pixel, or at 16 bits.
    /// </summary>
    private static void WriteDisplayPng(string path, float[] linear, double exposureEv, PreviewRequest request, PixelWindow window)
    {
        if (request.BitDepth == 16)
        {
            PngEncoder.Write16(path, DisplayTransform.ToSrgb16(linear, exposureEv, request.Display, request.Look), window.Width, window.Height, PngColor.Rgb);
            return;
        }
        PngEncoder.Write(path, DisplayTransform.ToSrgb8(linear, exposureEv, request.Display, request.Look, request.Dither, window), window.Width, window.Height,
            PngColor.Rgb);
    }

    /// <summary>
    /// The slider pins of <paramref name="request"/> and where they came from: its slider file, or the recipe's own place and time
    /// fields, or none — with the slider options' overrides on top. A recipe that pins sliders itself cannot be given with a slider
    /// file: the pins would have two homes.
    /// </summary>
    internal static (SliderPins Pins, string From) ReadPins(Recipe recipe, PreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(request);
        SliderPins Parse(string text) => VistaJson.ParseSliders(JsonOverrides.Apply(text, request.SliderOverrides));
        if (request.SlidersPath is string path)
        {
            if (recipe.HasPins)
                throw new InvalidDataException(
                    $"{request.RecipePath}: the recipe pins sliders itself (its time, season, place or altitude); with --sliders, pin them in the slider file and leave the recipe its set pieces.");
            return (ReadInput(path, Parse), request.SliderOverrides.Count > 0 ? "slider file and options" : "slider file");
        }
        string from = recipe.HasPins ? "recipe" : "none";
        if (request.SliderOverrides.Count > 0)
            from += " and options";
        return (Parse(VistaJson.Serialize(SliderPins.FromRecipe(recipe))), from);
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

    /// <summary>
    /// The auto camera's detail budget over <paramref name="output"/> (vista-plan §10, M1.14's follow-up; <see cref="CompositionScorer.Detail"/>):
    /// the beauty as the PNG shows it, the ground that counts against the budget tinted magenta, lit meadow and forest past the same
    /// threshold yellow; and what it adds up to, the shares at other thresholds and by class too, for the sidecar.
    /// </summary>
    private (byte[] Rgb, DetailInfo Info) DetailImage(RenderOutput output, float[] linear, double exposureEv, PreviewRequest request, CameraSpec camera,
        SunPosition sun, HeightField terrain, double groundRadius)
    {
        CompositionLimits limits = CompositionLimits.Default;
        var probe = new TerrainSight(terrain, request.Mesh.FarField ? _worlds.FarTerrain() : null, groundRadius);
        CompositionBuffer buffer = CompositionBuffer.FromRender(output, camera, output.Width, output.Height, null, groundRadius);
        DetailMap map = CompositionScorer.Detail(buffer, camera, sun, limits, probe, DetailInfo.Thresholds.Min());
        byte[] rgb = DisplayTransform.ToSrgb8(linear, exposureEv, request.Display, request.Look);
        GroundClass[] others = [GroundClass.Meadow, GroundClass.Forest];
        for (int p = 0; p < map.CellPixels.Length; p++)
        {
            (byte R, byte G, byte B)? tint = map.Counts(p) ? ((byte)255, (byte)0, (byte)170)
                : map.CellPixels[p] > limits.DetailCellPixels && others.Contains(buffer.Class[p]) ? ((byte)255, (byte)220, (byte)0) : null;
            if (tint is not { } t)
                continue;
            rgb[p * 3] = (byte)((rgb[p * 3] + t.R) / 2);
            rgb[p * 3 + 1] = (byte)((rgb[p * 3 + 1] + t.G) / 2);
            rgb[p * 3 + 2] = (byte)((rgb[p * 3 + 2] + t.B) / 2);
        }
        var byThreshold = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (double threshold in DetailInfo.Thresholds)
            byThreshold[threshold.ToString("00.#", CultureInfo.InvariantCulture)] = map.ShareAt(threshold);
        var byClass = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (GroundClass c in (GroundClass[])[GroundClass.Rock, GroundClass.Scree, GroundClass.Snow, GroundClass.Meadow, GroundClass.Forest])
            byClass[JsonNamingPolicy.CamelCase.ConvertName(c.ToString())] = map.ShareAt(limits.DetailCellPixels, [c]);
        return (rgb, new DetailInfo
        {
            FinalFrameWidth = limits.FinalFrameWidth,
            FinalFrameHeight = limits.FinalFrameHeight,
            CellPixels = limits.DetailCellPixels,
            MaxShare = limits.DetailMaxShare,
            Share = map.Share,
            Breaks = map.Share > limits.DetailMaxShare,
            ShareByCellPixels = byThreshold,
            ShareByClass = byClass,
        });
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
            DebugOutput.Material => DebugImages.Material(Buffer(Aov.MaterialId), materials),
            DebugOutput.Tier => DebugImages.Tier(Buffer(Aov.MaterialId), materials),
            DebugOutput.Variance => DebugImages.Variance(Buffer(Aov.Variance)),
            _ => throw new ArgumentOutOfRangeException(nameof(debug), debug, "Unknown debug output."),
        };
    }

    /// <summary>The material constants of a render of <paramref name="field"/> under <paramref name="ground"/>'s grass line, tree line and pole.</summary>
    private static MaterialConstants MaterialConstantsOf(MaterialField field, GroundCover ground) =>
        field.Apply(MaterialRules.Default.Constants((float)ground.GrassLineMetres, ground.Poleward, field.NoiseSeed, (float)ground.TreeLineMetres, ground.Forest))
            .WithSnow(ground.SnowPlacement);

    /// <summary>
    /// Where the forest of <paramref name="ground"/> stands on a world whose height 0 lies <paramref name="groundAltitude"/> above sea
    /// level (vista-plan §10 M1.12b): what the mesh's crowns follow; none with <c>--forest off</c>.
    /// </summary>
    public static ForestPlacement ForestOf(GroundCover ground, double groundAltitude) =>
        ground.Forest ? new ForestPlacement(true, (float)ground.TreeLineMetres, ground.Poleward, (float)groundAltitude) : ForestPlacement.None;

    /// <summary>The shares of the world grid each material covers, from every sample's weights (schema 17; the forest's since schema 19).</summary>
    private static MaterialShares SharesOf(MaterialField field, HeightField terrain, MaterialWeights[] weights)
    {
        double hard = 0.0, soft = 0.0, scree = 0.0, soil = 0.0, grass = 0.0, wet = 0.0, forest = 0.0, snow = 0.0, firn = 0.0, ice = 0.0;
        int n = terrain.Size;
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                MaterialWeights w = weights[j * n + i];
                float x = (float)(field.Origin + i * field.Cell), z = (float)(field.Origin + j * field.Cell);
                if (field.BedAt(x, z, terrain[i, j]).Hardness >= 0.5f)
                    hard += w.Rock;
                else
                    soft += w.Rock;
                scree += w.Scree;
                soil += w.Soil;
                grass += w.Grass;
                forest += w.Forest;
                snow += w.Snow;
                firn += w.Firn;
                ice += w.Ice;
                wet += w.Wet;
            }
        }
        double count = (double)n * n;
        return new MaterialShares(hard / count, soft / count, scree / count, soil / count, grass / count, wet / count)
        {
            Forest = forest / count,
            Snow = snow / count,
            Firn = firn / count,
            Ice = ice / count,
        };
    }

    /// <summary>
    /// What the sidecar says of the ground's materials (schema 17): each one's albedo as the render's tables have it (<paramref name="tables"/>,
    /// a spectrum's colour through the render's quadrature since M1.12a).
    /// </summary>
    private static MaterialsInfo MaterialsInfoOf(IntegratorTables tables, IReadOnlyList<VistaMaterial> materials, TerrainMesh mesh, GroundCover ground,
        Weather.ClimateNormals climate, MaterialShares? shares)
    {
        var table = materials.Select(m => new MaterialEntry(m.Key, m.Tier, m.Roughness,
            [tables.Albedo[m.Id * 4], tables.Albedo[m.Id * 4 + 1], tables.Albedo[m.Id * 4 + 2]])).ToList();
        if (mesh.Materials is null)
            return new MaterialsInfo { ByRule = false, Table = table };
        MaterialDetailInfo? detail = null;
        if (mesh.Info.Detail)
        {
            var kinds = new MaterialDetail(0, mesh.Materials);
            detail = new MaterialDetailInfo(kinds.Rock.MaxAmplitude, kinds.Scree.MaxAmplitude, kinds.Smooth.MaxAmplitude,
                mesh.Materials.RockGradient, mesh.Materials.ScreeGradient, mesh.Materials.SmoothGradient)
            {
                CanopyAmplitudeMetres = mesh.Materials.Forest.Grows ? kinds.Canopy.MaxAmplitude : 0.0,
                CanopyGradientPerMetre = mesh.Materials.CanopyGradient,
            };
        }
        return new MaterialsInfo
        {
            ByRule = true,
            Table = table,
            Rules = MaterialRules.Default,
            GrassLineMetres = ground.GrassLineMetres,
            GrassLineFrom = string.Create(CultureInfo.InvariantCulture,
                $"where {climate.Station}'s warmest month averages {Weather.GrassLine.WarmestMonthCelsius:0.#} °C along the lapse rate"),
            Poleward = ground.Poleward,
            Forest = ground.Forest,
            TreeLineMetres = ground.TreeLineMetres,
            TreeLineFrom = string.Create(CultureInfo.InvariantCulture,
                $"where {climate.Station}'s warmest month averages {Weather.TreeLine.WarmestMonthCelsius:0.#} °C along the lapse rate"),
            Detail = detail,
            Shares = shares,
            Snow = ground.Snow,
            SnowlineMetres = ground.SnowlineMetres,
            FreezingLevelMetres = ground.FreezingLevelMetres,
            GlacierLineMetres = mesh.Materials.Glaciated ? ground.GlacierLineMetres : null,
        };
    }

    /// <summary>The integrator's spectral tables for <paramref name="materials"/> on <paramref name="wavelengths"/>, built once per pair.</summary>
    private IntegratorTables Tables(IReadOnlyList<VistaMaterial> materials, WavelengthTable wavelengths)
    {
        if (_tables is not { } kept || !ReferenceEquals(kept.Materials, materials) || !ReferenceEquals(kept.Wavelengths, wavelengths))
            _tables = kept = (materials, wavelengths, IntegratorTables.Build(materials, wavelengths));
        return kept.Tables;
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
        GroundCover Ground,
        Quadrature Quadrature,
        LowAirKey? LowAir,
        RenderSettings Settings)
    {
        /// <summary>The <see cref="VistaScene"/> fields the key leaves out, because the renderer does not read them.</summary>
        internal static IReadOnlySet<string> SceneFieldsNotRead { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(VistaScene.Recipe), nameof(VistaScene.Seed),
        };

        /// <summary>The key of rendering <paramref name="scene"/> with <paramref name="settings"/>.</summary>
        public static RenderKey Of(VistaScene scene, RenderSettings settings) => new(
            scene.Mesh, scene.Terrain, scene.Camera with { ExposureEv = 0.0 }, scene.Sun, scene.Materials, scene.Air, scene.GroundAltitudeMetres, scene.Ground,
            scene.Quadrature, scene.LowAir?.Key, settings);
    }

    /// <summary>What a denoised image is made from: the render, the settings and the input scale (its bits; NaN lets the filter choose).</summary>
    private sealed record DenoiseKey(RenderKey Render, DenoiseSettings Settings, int InputScaleBits);
}
