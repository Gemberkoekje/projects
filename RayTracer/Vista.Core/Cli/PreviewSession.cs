using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.Sliders;
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

        CameraSpec camera = request.CameraPath is null
            ? DefaultCamera.For(terrain)
            : ReadInput(request.CameraPath, VistaJson.ParseCamera);
        if (request.CameraOverrides.Count > 0)
            camera = VistaJson.ParseCamera(JsonOverrides.Apply(VistaJson.Serialize(camera), request.CameraOverrides));
        if (DefaultCamera.GroundWarning(camera, terrain) is { } groundWarning)
            warnings.Add(groundWarning);

        // The linked sliders (vista-plan §10 M1.11): the pins, resolved with the set pieces into the place, the sun and the weather.
        (SliderPins pins, string pinsFrom) = ReadPins(recipe, request);
        SliderState sliders = LinkedSliders.Resolve(recipe, pins, AtmosphereModel.Default, world.Footing);
        warnings.AddRange(sliders.Hints);
        Almanac almanac = sliders.Almanac;
        IReadOnlyList<VistaMaterial> materials = MaterialCatalog.For(request.Mesh.Materials);
        GroundCover ground = GroundCover.For(sliders.Climate, almanac.Place.LatitudeDegrees) with { Forest = request.Forest };
        AtmosphereModel air = sliders.Air(AtmosphereModel.Default);
        double groundAltitude = sliders.BaseAltitudeMetres;
        string altitudeFrom = sliders.Readings[Slider.Altitude].From;

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

        string directory = Path.GetDirectoryName(Path.GetFullPath(request.OutPath))!;
        string baseName = Path.GetFileNameWithoutExtension(request.OutPath);
        Directory.CreateDirectory(directory);
        string Sibling(string suffix) => Path.Combine(directory, baseName + suffix);

        RenderInfo? renderInfo = null;
        ExposureInfo? exposure = null;
        MaterialShares? shares = null;
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
                (byte[] drainage, int drainageSize) = DebugImages.DrainageImage(erosion.Surface, erosion.Drainage, camera);
                PngEncoder.Write(Sibling(".drainage.png"), drainage, drainageSize, drainageSize, PngColor.Rgb);
                outputs["drainageMap"] = baseName + ".drainage.png";
                (byte[] hardness, int hardnessSize) = DebugImages.HardnessImage(erosion, camera);
                PngEncoder.Write(Sibling(".hardness.png"), hardness, hardnessSize, hardnessSize, PngColor.Rgb);
                outputs["hardnessMap"] = baseName + ".hardness.png";
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
            var scene = new VistaScene(recipe, request.Seed, camera, terrain, mesh, almanac.Sun, materials, air, groundAltitude)
            {
                Ground = ground,
                Quadrature = request.Quadrature,
            };
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

            // The exposure meter reads neither the size nor the loupe, so every size and loupe of this view is exposed alike and
            // the meter is reused across them; the camera's compensation and the meter's own (M1.11b) are applied after it. The
            // incident meter (M1.10a) reads the air's LUTs the render just built: the light on a grey card where the camera stands.
            // The average meter renders the whole frame at a fixed size.
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
                    light = _renderer.MeasureIncidentLight(scene);
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
                    metered = _renderer.Render(scene, meter, new HashSet<Aov>(), new BatchPlanner(meter.Width, meter.Height, meter.SamplesPerPixel, meter.Batching));
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
            exposure = exposure with { CompensationEv = camera.ExposureEv, MeterCompensationEv = meterCompensation };
            timings["exposureMeter"] = Lap(stopwatch);
            MarkRendererStages(stages, rendered, built, settings);

            float[] linear = DisplayTransform.XyzToLinearRec709(output.Xyz);
            WriteDisplayPng(request.OutPath, linear, exposure.AppliedEv, request, window);
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
            },
            Place = almanac.Place,
            Sliders = SlidersInfo.From(sliders, request.SlidersPath, pinsFrom),
            Sun = SunInfo.From(almanac, frame),
            Atmosphere = AtmosphereInfo.From(frame, settings.Atmosphere, altitudeFrom),
            Exposure = exposure,
            Denoise = denoised,
            DataTiers = MaterialCatalog.TiersOf(materials),
            Materials = MaterialsInfoOf(Tables(materials, WavelengthTable.For(request.Quadrature)), materials, mesh, ground, sliders.Climate, shares),
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
        field.Apply(MaterialRules.Default.Constants((float)ground.GrassLineMetres, ground.Poleward, field.NoiseSeed, (float)ground.TreeLineMetres, ground.Forest));

    /// <summary>
    /// Where the forest of <paramref name="ground"/> stands on a world whose height 0 lies <paramref name="groundAltitude"/> above sea
    /// level (vista-plan §10 M1.12b): what the mesh's crowns follow; none with <c>--forest off</c>.
    /// </summary>
    public static ForestPlacement ForestOf(GroundCover ground, double groundAltitude) =>
        ground.Forest ? new ForestPlacement(true, (float)ground.TreeLineMetres, ground.Poleward, (float)groundAltitude) : ForestPlacement.None;

    /// <summary>The shares of the world grid each material covers, from every sample's weights (schema 17; the forest's since schema 19).</summary>
    private static MaterialShares SharesOf(MaterialField field, HeightField terrain, MaterialWeights[] weights)
    {
        double hard = 0.0, soft = 0.0, scree = 0.0, soil = 0.0, grass = 0.0, wet = 0.0, forest = 0.0;
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
                wet += w.Wet;
            }
        }
        double count = (double)n * n;
        return new MaterialShares(hard / count, soft / count, scree / count, soil / count, grass / count, wet / count) { Forest = forest / count };
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
            scene.Quadrature, settings);
    }

    /// <summary>What a denoised image is made from: the render, the settings and the input scale (its bits; NaN lets the filter choose).</summary>
    private sealed record DenoiseKey(RenderKey Render, DenoiseSettings Settings, int InputScaleBits);
}
