using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Vista.App.Denoise;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --gpu-selftest</c>: the checks that need the RTX 3070 (vista-plan §10), headless with a pass/fail
/// exit code, like the maze's <c>--phase6-selftest</c>. One fixed scene (snow-capped mountains at golden hour,
/// seed 42, M0's default camera for seed 42, 320×180, 16 spp, every debug output) is rendered four times:
/// <list type="number">
///   <item>the reference, at the default dispatch budget — it must be sane (finite, lit, sky and terrain both
///   in frame, more rays than pixel-samples);</item>
///   <item>the same again — bit-identical, same ray count;</item>
///   <item>under a 0.05 ms budget, so row bands of one sample replace full-frame passes of many — bit-identical,
///   same ray count (the batch partition must not change the image);</item>
///   <item>with no debug outputs — the beauty is bit-identical (asking for debug images never changes it).</item>
/// </list>
/// Then the loupe (M1.1): a 640×360 frame, a 64×64 window of it straddling a peak and the sky, and that window
/// again under the 0.05 ms budget. The window must equal the same pixels cut from the frame, bit for bit in
/// the beauty and every debug output, however it is split into dispatches.
///
/// <para>Then the atmosphere (M1.3, M1.4): the LUT build is bit-identical twice; texels of each GPU LUT match their C#
/// reference (<see cref="AtmosphereLuts"/>) fed the same input LUTs, and so does the aerial perspective of chosen world
/// segments (<see cref="AerialPerspective"/>); a twilight render (the sun 4° below the horizon) is lit, from the sky
/// alone; and the reference atmosphere, with and without the terrain's shadow in the air, is bit-identical twice and
/// under the 0.05 ms budget.</para>
///
/// <para>Then the sky as a light (M1.5; every render above already sampled it): the sky distribution is rebuilt
/// bit-identically; its GPU measurement matches <see cref="SkyDistribution.CellLuminance"/> on the same sky view; the GPU's
/// sampler and pdf match <see cref="SkyDistribution"/>'s; and MIS and M1.4's estimator agree on the image's mean within its
/// noise, over independent renders.</para>
///
/// <para>Then the incident meter (M1.10a): the GPU's measurement of the light on the grey card matches its C# reference on the same
/// LUTs, is bit-identical when the LUTs are rebuilt, has the sun outshine the sky at golden hour, and the sky alone 4° after sunset.</para>
///
/// <para>Then what watch mode relies on (M1.8): after a change of the time of day, the camera's height or the base altitude, a
/// renderer that rebuilt only the part of the air the change touched renders bit-identically to one that rebuilt all of it.</para>
///
/// <para>First of all the world (M1.7): the erosion passes against their C# reference on a 65² patch, and the self-test's world
/// eroded twice and through a cache round trip, bit-identical each time, its drainage included (see <c>ErosionChecks</c>); the
/// scene above renders that eroded world, and every committed view in <c>vista-views/</c> must stand above it (M1.11a).</para>
///
/// <para>Then the denoiser (M1.6), when Open Image Denoise has been fetched: the reference render denoised twice on the CPU is
/// bit-identical, sane and as bright as the raw image; on CUDA, bit-identical twice and as bright as the CPU's.</para>
///
/// <para>Since M1.12 the scene has the ground's materials by rule and their detail, so every check above renders them; and the
/// materials themselves (<see cref="MaterialChecks"/>): the world's material field is the same twice, the shader's rules match the
/// C# reference at random places of the world, and the reference render shows several materials.</para>
///
/// <para>Then the estimate's video memory (M1.15, <see cref="MemoryChecks"/>): a fresh renderer plans the 640×360 frame with every output,
/// renders it, and the peak it measured lies within the plan's stated margin.</para>
/// </summary>
internal static partial class GpuSelfTest
{
    /// <summary>Largest relative difference a GPU LUT texel may have from its C# reference (float against double).</summary>
    private const double LutTolerance = 2e-3;

    /// <summary>
    /// The camera every render here uses: the default measurement view for seed 42 (<c>vista-views/default.camera.json</c>), 6 m
    /// above a meadow, looking north-north-west at the range with the sky above it. <see cref="DefaultCamera.For"/>'s pose has stood on a
    /// valley floor facing a wall since M1.11a's incision; M1.11d re-placed the view (M0's pose is underground on its world), M1.12c
    /// again (M1.11d's is underground on the world its folded strata shape), and M1.13 once more (the glaciers' carving left M1.12c's
    /// 24 m over new fill): on a meadow facing the glaciated range, 2 m above the incised ground CI checks it over. M1a.2's finer
    /// detail on the incision's faces raised that ground 18 m, and the view with it (6.5 m above the eroded ground).
    /// </summary>
    private static readonly CameraSpec SelfTestCamera = new()
    {
        Position = new WorldPoint(4450.0, 1716.8, -8825.0),
        YawDegrees = -20.0,
        PitchDegrees = 3.0,
    };

    /// <summary>The seasonal snowline of the self-test's scene, m above sea level: spring snow over the upper half of the range (M1.13).</summary>
    private const double SelfTestSnowlineMetres = 2600.0;

    /// <summary>Runs the checks, writing a report to <paramref name="log"/>; returns 0 when every check passes.</summary>
    public static int Run(TextWriter log)
    {
        var recipe = new Recipe
        {
            Name = "gpu self-test",
            SetPieces = [SetPiece.SnowCappedMountains],
            Time = TimeOfDay.GoldenHour,
        };
        const ulong seed = 42;
        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}, driver {renderer.DriverVersion}");
        bool pass = ErosionChecks(renderer, recipe, seed, log, out ErosionResult eroded);
        HeightField terrain = eroded.Surface;
        pass &= ViewChecks(terrain, log);
        Almanac almanac = Almanac.For(recipe);
        double ground = Places.BaseAltitude(recipe, almanac.Place).Metres;
        var settings = new RenderSettings { Width = 320, Height = 180, SamplesPerPixel = 16, MaxBounces = 6 };
        CameraSpec camera = SelfTestCamera;
        // Spring snow down to SelfTestSnowlineMetres and the world's glaciers (vista-plan §10 M1.13), so the parity check reaches both.
        GroundCover cover = GroundCover.For(Weather.Climate.For(almanac.Place), almanac.Place.LatitudeDegrees) with
        {
            Snow = true,
            SnowlineMetres = SelfTestSnowlineMetres,
            FreezingLevelMetres = SelfTestSnowlineMetres + 300.0,
            GlacierLineMetres = ErosionSettings.For(TerrainProfile.For(recipe.SetPieces)).GlacierElaMetres + ground,
        };
        MaterialField materials = MaterialFieldOf(recipe, seed, eroded, cover, ground);
        TerrainHierarchy hierarchy = TerrainHierarchy.Build(terrain, TerrainProfile.For(recipe.SetPieces), seed, new MaterialDetail(seed, materials), materials);
        TerrainMesh mesh = MeshFor(hierarchy, camera, settings, ground);
        var scene = new VistaScene(recipe, seed, camera, terrain, mesh, almanac.Sun, MaterialCatalog.Ground, AtmosphereModel.Default, ground)
        {
            Ground = cover,
        };
        pass &= MeshChecks(hierarchy, scene, settings, log);
        var allAovs = new HashSet<Aov>(Enum.GetValues<Aov>());

        RenderOutput reference = Render(renderer, scene, settings, allAovs, new BatchPlannerOptions(), log, "reference");
        RenderOutput again = Render(renderer, scene, settings, allAovs, new BatchPlannerOptions(), log, "again");
        // A pessimistic first guess keeps the early dispatches to a few rows each.
        var tight = new BatchPlannerOptions { DispatchBudgetMs = 0.05, InitialCostNs = 20000 };
        RenderOutput split = Render(renderer, scene, settings, allAovs, tight, log, "0.05 ms budget");
        RenderOutput bare = Render(renderer, scene, settings, new HashSet<Aov>(), new BatchPlannerOptions(), log, "no debug outputs");

        pass &= Check(log, "reference is sane", Sanity(reference, settings));
        pass &= Check(log, "same render twice is bit-identical", Same(reference, again, compareAovs: true));
        // A dispatch covers whole rows and at least one sample: more dispatches than samples means some pass
        // was split into row bands, fewer means some dispatch took several samples at once.
        pass &= Check(log, "the two budgets split the render differently",
            reference.Stats.Dispatches < settings.SamplesPerPixel && split.Stats.Dispatches > settings.SamplesPerPixel
                ? null
                : $"{reference.Stats.Dispatches} and {split.Stats.Dispatches} dispatches for {settings.SamplesPerPixel} samples");
        pass &= Check(log, "0.05 ms budget is bit-identical", Same(reference, split, compareAovs: true));
        pass &= Check(log, "debug outputs never change the beauty", Same(reference, bare, compareAovs: false));
        pass &= MaterialChecks(renderer, recipe, seed, eroded, materials, hierarchy, scene, reference, log);
        pass &= AutoCameraChecks(recipe, seed, eroded, materials, scene, reference, log);

        // The loupe: off the 8-pixel thread-group grid, with the skyline in it (since M1.13's pose the range's skyline crosses it
        // near row 175, half of it sky).
        var frame = settings with { Width = 640, Height = 360 };
        var window = new PixelWindow(291, 139, 64, 64);
        RenderOutput full = Render(renderer, scene, frame, allAovs, new BatchPlannerOptions(), log, "640×360 frame");
        RenderOutput loupe = Render(renderer, scene, frame with { Window = window }, allAovs, new BatchPlannerOptions(), log, $"loupe {window}");
        RenderOutput loupeSplit = Render(renderer, scene, frame with { Window = window }, allAovs, tight, log, $"loupe {window}, 0.05 ms budget");
        pass &= Check(log, "the loupe sees both sky and terrain", Mixed(loupe));
        pass &= Check(log, "the loupe is that window of the full frame, bit for bit", SameAsCrop(full, loupe, window));
        pass &= Check(log, "the loupe under a 0.05 ms budget is bit-identical", Same(loupe, loupeSplit, compareAovs: true));
        pass &= MemoryChecks(scene, frame, allAovs, log);

        pass &= AtmosphereChecks(renderer, scene, settings, tight, log);
        pass &= LowAirChecks(renderer, recipe, seed, eroded, scene, settings, log);
        pass &= WatchChecks(renderer, hierarchy, scene, settings with { Width = 160, Height = 90 }, log);
        pass &= DenoiseChecks(reference, log);
        log.WriteLine(pass ? "PASS" : "FAIL");
        return pass ? 0 : 1;
    }

    /// <summary>
    /// The world stage (M1.7), first, since every render below draws its world: the GPU's erosion passes against the C#
    /// reference (<see cref="ErosionParity"/>); then the self-test's world eroded on the GPU, stored in a fresh cache, eroded
    /// again and read back from the cache — all three the same bits; then, when the default cache holds this world, whether
    /// this GPU and driver still erode it to the bits the cache holds (a driver update that moves the world fails here instead of
    /// passing silently). <paramref name="eroded"/> is the erosion's result: the surface and its maps.
    /// </summary>
    private static bool ErosionChecks(D3D12VistaRenderer renderer, Recipe recipe, ulong seed, TextWriter log, out ErosionResult eroded)
    {
        WorldGrid grid = WorldGrid.Default;
        TerrainProfile profile = TerrainProfile.For(recipe.SetPieces);
        ErosionSettings erosion = ErosionSettings.For(profile);
        bool pass = ErosionParity.Run(renderer, erosion, grid.CellSize, log);

        string folder = Path.Combine(Path.GetTempPath(), "vista-selftest-worlds-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        try
        {
            var cache = new ErosionCache(folder);
            TerrainWorld world = WorldBuilder.Build(recipe, seed, grid, renderer, cache);
            ErosionResult first = world.Erosion!;
            eroded = first;
            ErosionStats stats = world.Info.Stats!;
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  eroded {grid.Size}\u00b2 \u00d7 {grid.CellSize} m in {stats.Seconds:0.00} s GPU ({stats.WallSeconds:0.00} s wall, {stats.Submissions} submissions, " +
                $"longest {stats.LongestSubmissionSeconds * 1e3:0} ms), {stats.StateBytes / 1048576.0:0} MiB of state; hash {first.Surface.ContentHash():x16}"));
            pass &= Check(log, "the eroded world is sane", first.Surface.Heights.ToArray().All(float.IsFinite)
                && first.Loose.All(v => v >= 0f && float.IsFinite(v)) && first.StandingWater.All(v => v >= 0f && float.IsFinite(v))
                && world.Info.Stats!.LongestSubmissionSeconds < 1.5 ? null : "a value is negative or not finite, or a submission came near the GPU timeout");

            HeightField ground = TerrainGenerator.Generate(recipe, seed, grid);
            (HeightField prepared, StrataGrid strata) = WorldBuilder.Prepare(ground, recipe, seed, erosion);
            // The builder lays today's glaciers on what the eroder returns (M1.13); so does this second erosion.
            ErosionResult second = Glaciers.Lay(renderer.Erode(prepared, erosion, strata).Result, erosion);
            pass &= Check(log, "the world eroded twice is bit-identical", SameWorld(first, second));

            (ErosionResult Result, WorldOrigin Origin)? loaded = cache.TryLoad(profile, seed, grid, erosion, out string? problem);
            pass &= Check(log, "the world read back from the cache is bit-identical",
                loaded is { } l ? SameWorld(first, l.Result) : problem ?? "the cache does not hold it");

            if (ErosionCache.Default.TryLoad(profile, seed, grid, erosion, out _) is { } cached)
            {
                pass &= Check(log, "this GPU and driver erode the cached world to the same bits", SameWorld(first, cached.Result) is null ? null
                    : $"the cache's copy was eroded on {cached.Origin.Device} (driver {cached.Origin.Driver}, {cached.Origin.CreatedUtc}); " +
                      $"this is {renderer.DeviceName} (driver {renderer.DriverVersion}). The driver moved the world: re-baseline by deleting the cached file.");
            }
            else
            {
                log.WriteLine("[skip] the default cache does not hold the self-test world, so a driver change cannot be checked against it");
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        return pass;
    }

    /// <summary>
    /// The material field of the self-test's world (vista-plan §10 M1.12), as a preview session builds it, with the detail weights of
    /// <paramref name="cover"/>'s forest on a world whose height 0 lies <paramref name="groundAltitude"/> above sea level (M1.12b).
    /// </summary>
    private static MaterialField MaterialFieldOf(Recipe recipe, ulong seed, ErosionResult eroded, GroundCover cover, double groundAltitude)
    {
        StrataGrid strata = WorldBuilder.StrataOf(recipe, seed, eroded.Surface, eroded.Strata, out RockHardness rock);
        return MaterialField.For(eroded.Surface, eroded, strata, rock.Tints, MaterialRules.Default, MaterialNoise.SeedOf(seed))
            .WithForest(eroded.Surface, Cli.PreviewSession.ForestOf(cover, groundAltitude), MaterialRules.Default);
    }

    /// <summary>
    /// The ground's materials (vista-plan §10 M1.12): the world's material field built again is the same bits (its maps, its beds and
    /// its detail weights); the shader's rules (<c>CSMaterialProbe</c>) match the C# reference (<see cref="MaterialField.Inputs"/>,
    /// <see cref="MaterialRules.Evaluate"/>) at 4096 random places of the world grid, each on its ground and up to 40 m off it, within
    /// 0.002 of a weight and of the hardness and 0.02° of slope (single-precision transcendentals on both sides; the shader's arc cosine
    /// is an approximation, worst on nearly flat ground, where a weight barely follows the slope); and the reference render shows at
    /// least three materials.
    /// </summary>
    private static bool MaterialChecks(D3D12VistaRenderer renderer, Recipe recipe, ulong seed, ErosionResult eroded, MaterialField materials,
        TerrainHierarchy hierarchy, VistaScene scene, RenderOutput reference, TextWriter log)
    {
        MaterialField again = MaterialFieldOf(recipe, seed, eroded, scene.Ground, scene.GroundAltitudeMetres);
        var probe = new double[] { 0.0, 0.25, 0.5, 0.75, 1.0 };
        bool sameDetail = probe.All(u => probe.All(v =>
            again.DetailWeightsAt(materials.Origin + u * (materials.Size - 1) * materials.Cell, materials.Origin + v * (materials.Size - 1) * materials.Cell)
            == materials.DetailWeightsAt(materials.Origin + u * (materials.Size - 1) * materials.Cell, materials.Origin + v * (materials.Size - 1) * materials.Cell)));
        bool pass = Check(log, "the material field built twice is bit-identical",
            again.Packed.SequenceEqual(materials.Packed) && again.Beds.SequenceEqual(materials.Beds) && sameDetail
            && again.RockGradient == materials.RockGradient && again.ScreeGradient == materials.ScreeGradient
            && again.CanopyGradient == materials.CanopyGradient ? null : "the two builds differ");

        var random = new Random(1212);
        HeightField surface = eroded.Surface;
        var places = new List<(float X, float Z, float Height)>();
        for (int i = 0; i < 4096; i++)
        {
            double x = surface.OriginX + random.NextDouble() * surface.Extent, z = surface.OriginZ + random.NextDouble() * surface.Extent;
            places.Add(((float)x, (float)z, (float)(surface.HeightAt(x, z) + (i % 2 == 0 ? 0.0 : random.NextDouble() * 40.0))));
        }
        float[] gpu = renderer.RunMaterialProbe(scene, places);
        MaterialConstants k = D3D12VistaRenderer.MaterialConstantsFor(scene, DiffuseModel.OrenNayar, CanopyModel.Kernels, 1080);
        double worstWeight = 0.0, worstHardness = 0.0, worstSlope = 0.0;
        int forested = 0, snowy = 0, glaciated = 0;
        for (int i = 0; i < places.Count; i++)
        {
            (float x, float z, float height) = places[i];
            (float nx, float nz) = hierarchy.MapNormal(x, z);
            MaterialInputs inputs = materials.Inputs(x, z, height, nx, nz, (float)scene.GroundAltitudeMetres, k);
            MaterialWeights w = MaterialRules.Evaluate(inputs, k);
            ReadOnlySpan<float> g = gpu.AsSpan(i * 12, 12);
            foreach ((float a, float b) in new[]
            {
                (w.Rock, g[0]), (w.Scree, g[1]), (w.Soil, g[2]), (w.Grass, g[3]), (w.Wet, g[4]), (w.Stony, g[5]), (w.Forest, g[8]), (w.Snow, g[9]),
                (w.Firn, g[10]), (w.Ice, g[11]),
            })
                worstWeight = Math.Max(worstWeight, Math.Abs(a - b));
            worstHardness = Math.Max(worstHardness, Math.Abs(inputs.Hardness - g[6]));
            worstSlope = Math.Max(worstSlope, Math.Abs(inputs.SlopeDegrees - g[7]));
            forested += w.Forest > 0.5f ? 1 : 0;
            snowy += w.Snow > 0.5f ? 1 : 0;
            glaciated += w.Firn + w.Ice > 0.5f ? 1 : 0;
        }
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  materials at {places.Count} places ({forested} mostly forest, {snowy} mostly snow, {glaciated} mostly glacier): worst weight {worstWeight:G3}, hardness {worstHardness:G3}, slope {worstSlope:G3}°"));
        pass &= Check(log, "the shader's material rules match the C# reference", worstWeight <= 2e-3 && worstHardness <= 2e-3 && worstSlope <= 0.02 ? null
            : string.Create(CultureInfo.InvariantCulture, $"worst weight {worstWeight:G3}, hardness {worstHardness:G3}, slope {worstSlope:G3}°"));
        // Seed 42's lowest valleys stand some 400 m above Zermatt's valley floor: 2.1% of its places are mostly forest (vista-log/M1.12b.md).
        pass &= Check(log, "the parity check reaches the forest", forested >= places.Count / 100 ? null
            : string.Create(CultureInfo.InvariantCulture, $"only {forested} of {places.Count} places are mostly forest"));
        pass &= Check(log, "the parity check reaches the snow and the glaciers",
            snowy >= places.Count / 100 && (!materials.Glaciated || glaciated >= places.Count / 200) ? null
            : string.Create(CultureInfo.InvariantCulture, $"{snowy} places mostly snow and {glaciated} mostly glacier of {places.Count}"));

        var seen = new HashSet<int>();
        foreach (float id in reference.Aovs[Aov.MaterialId])
        {
            if (id >= 0f)
                seen.Add((int)id);
        }
        pass &= Check(log, "the reference render shows several materials", seen.Count >= 3 ? null
            : $"only {string.Join(", ", seen.Order().Select(id => MaterialCatalog.Ground[id].Key))}");
        return pass;
    }

    /// <summary>Null when the two worlds are the same bits, else what differs.</summary>
    private static string? SameWorld(ErosionResult a, ErosionResult b)
    {
        if (!a.Surface.Heights.SequenceEqual(b.Surface.Heights))
            return "the surfaces differ";
        if (!a.Loose.AsSpan().SequenceEqual(b.Loose) || !a.Discharge.AsSpan().SequenceEqual(b.Discharge)
            || !a.WaterDepth.AsSpan().SequenceEqual(b.WaterDepth) || !a.StandingWater.AsSpan().SequenceEqual(b.StandingWater))
            return "the maps differ";
        if (!a.Drainage.Direction.AsSpan().SequenceEqual(b.Drainage.Direction) || !a.Drainage.FillDepth.AsSpan().SequenceEqual(b.Drainage.FillDepth))
            return "the drainage differs";
        if (!a.Hardness.AsSpan().SequenceEqual(b.Hardness) || a.Strata != b.Strata)
            return "the rock's hardness differs";
        return a.ContentHash() == b.ContentHash() ? null : "the hashes differ";
    }

    /// <summary>
    /// The committed views (every <c>*.camera.json</c> under <c>vista-views/</c>, found above the executable) stand above the eroded
    /// seed-42 ground (M1.11a: the incision moves the ground by hundreds of metres, and CI only sees the ground before the GPU
    /// erosion). Skipped when the folder is not there.
    /// </summary>
    private static bool ViewChecks(HeightField terrain, TextWriter log)
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "vista-views")))
            folder = folder.Parent;
        if (folder is null)
        {
            log.WriteLine("[skip] no vista-views folder above the executable, so the committed views cannot be checked");
            return true;
        }
        var problems = new List<string>();
        double lowest = double.MaxValue;
        string[] files = Directory.GetFiles(Path.Combine(folder.FullName, "vista-views"), "*.camera.json", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            CameraSpec camera = VistaJson.ParseCamera(File.ReadAllText(file));
            double clearance = camera.Position.Y - terrain.HeightAt(camera.Position.X, camera.Position.Z);
            lowest = Math.Min(lowest, clearance);
            if (clearance <= 0.0)
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(file)} is {-clearance:0.0} m underground"));
        }
        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {files.Length} committed views, the lowest {lowest:0.0} m above the eroded ground"));
        return Check(log, "every committed view stands above the eroded seed-42 ground", problems.Count == 0 ? null : string.Join("; ", problems));
    }

    /// <summary>
    /// The denoiser (M1.6) on the reference render: on the CPU, denoising the same input twice gives the same bits, and the image
    /// is finite, not negative, and as bright as the raw one within 2%; on CUDA, the same twice and as bright as the CPU's within
    /// 1%. Without Open Image Denoise next to the executable the checks are skipped (it is fetched, never committed); a CUDA
    /// device that cannot run is reported but fails nothing, since the CPU is the default.
    /// </summary>
    private static bool DenoiseChecks(RenderOutput reference, TextWriter log)
    {
        using var denoiser = new OidnDenoiser();
        // The reference is 320 px at 16 samples, the meter's own size, so it is its own exposure meter.
        ExposureInfo exposure = AutoExposure.Measure(reference.Xyz, 0.0);
        float[] linear = DisplayTransform.XyzToLinearRec709(reference.Xyz);
        var cpu = new DenoiseSettings { Device = DenoiseDevice.Cpu };
        DenoiseInput input = Denoising.Prepare(reference, linear, Denoising.InputScale(cpu, exposure), out _);
        DenoiseResult first = denoiser.Denoise(input, cpu);
        if (first.Problem == OidnDenoiser.MissingLibrary)
        {
            log.WriteLine($"[skip] the denoiser: {first.Problem}");
            return true;
        }
        bool pass = Check(log, "the denoiser runs on the CPU", first.Succeeded ? null : first.Problem);
        if (!first.Succeeded)
            return false;
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  denoised by {first.Run!.Library} {first.Run.Version} on {first.Run.Device}: {first.Run.ExecuteSeconds * 1e3:0.0} ms"));
        DenoiseResult again = denoiser.Denoise(input, cpu);
        pass &= Check(log, "denoising on the CPU twice is bit-identical",
            again.Succeeded && ImageError.BitIdentical(first.Color, again.Color) ? null : again.Problem.Length > 0 ? again.Problem : "the images differ");
        double bias = ImageError.MeanLuminanceBias(first.Color, linear);
        pass &= Check(log, "the denoised image is sane and keeps the raw one's brightness",
            first.Color.Any(c => !float.IsFinite(c) || c < 0f) ? "a value is negative or not finite"
            : Math.Abs(bias) > 0.02 ? string.Create(CultureInfo.InvariantCulture, $"mean luminance {bias:+0.00%;-0.00%} against the raw image") : null);

        var cuda = cpu with { Device = DenoiseDevice.Cuda };
        DenoiseResult gpu = denoiser.Denoise(input, cuda);
        if (!gpu.Succeeded)
        {
            log.WriteLine($"[note] the CUDA denoiser did not run: {gpu.Problem}");
            return pass;
        }
        DenoiseResult gpuAgain = denoiser.Denoise(input, cuda);
        pass &= Check(log, "denoising on CUDA twice is bit-identical",
            gpuAgain.Succeeded && ImageError.BitIdentical(gpu.Color, gpuAgain.Color) ? null : gpuAgain.Problem.Length > 0 ? gpuAgain.Problem : "the images differ");
        double fromCpu = ImageError.MeanLuminanceBias(gpu.Color, first.Color);
        pass &= Check(log, "CUDA's denoised image is as bright as the CPU's",
            Math.Abs(fromCpu) > 0.01 ? string.Create(CultureInfo.InvariantCulture, $"mean luminance {fromCpu:+0.00%;-0.00%} against the CPU's") : null);
        return pass;
    }

    private static bool AtmosphereChecks(D3D12VistaRenderer renderer, VistaScene scene, RenderSettings settings, BatchPlannerOptions tight, TextWriter log)
    {
        bool pass = true;
        AtmosphereFrame air = scene.AirFrame();
        renderer.ForgetAtmosphere();
        AtmosphereLutData luts = renderer.BuildAtmosphere(air, scene.Wavelengths(), captureOrder2: true, out double seconds);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  atmosphere LUTs: {seconds * 1e3:0.0} ms GPU"));
        renderer.ForgetAtmosphere();
        AtmosphereLutData rebuilt = renderer.BuildAtmosphere(air, scene.Wavelengths(), captureOrder2: false, out _);
        pass &= Check(log, "the LUT build is bit-identical twice",
            Differs(luts.Columns, rebuilt.Columns) ?? Differs(luts.Orders, rebuilt.Orders)
            ?? Differs(luts.GroundIrradiance, rebuilt.GroundIrradiance) ?? Differs(luts.SkyView, rebuilt.SkyView));

        AtmosphereTables tables = AtmosphereTables.Build(air.Model, scene.Wavelengths());
        ScatteringOrder2 order2 = luts.Order2!;
        pass &= Check(log, "columns LUT texels match the C# reference", ColumnsParity(air, luts));
        pass &= Check(log, "direct and order-1 ground irradiance match the C# reference", IrradianceParity(air, luts, order2, tables));
        pass &= Check(log, "single-scattering texels match the C# reference", SingleParity(air, luts, order2, tables));
        pass &= Check(log, "order-2 density texels match the C# reference", DensityParity(air, luts, order2, tables));
        pass &= Check(log, "order-2 radiance texels match the C# reference", RadianceParity(air, order2, tables));
        pass &= Check(log, "ground-irradiance entries match the C# reference", GroundParity(air, luts, tables));
        pass &= Check(log, "sky-view LUT texels match the C# reference", SkyParity(air, luts, tables));
        pass &= Check(log, "aerial perspective along world segments matches the C# reference", AerialParity(renderer, scene, air, luts, tables));
        pass &= SkyLightChecks(renderer, scene, settings, luts, log);

        // Twilight: 4° below the horizon the sun lights no surface, so all the light is the sky's.
        Almanac almanac = Almanac.For(scene.Recipe);
        double latitude = almanac.Place.LatitudeDegrees;
        double hours = SolarPosition.HoursAtElevation(latitude, almanac.Date, -4.0, morning: false).Hours;
        VistaScene twilight = scene with { Sun = SolarPosition.At(latitude, almanac.Date, hours) };
        var aovs = new HashSet<Aov> { Aov.MaterialId };
        RenderOutput dim = Render(renderer, twilight, settings, aovs, new BatchPlannerOptions(), log, "twilight, sun at −4°");
        pass &= Check(log, "twilight is lit by the sky alone", Twilight(dim));
        pass &= IncidentChecks(renderer, scene, twilight, luts, tables, log);

        // The reference atmosphere: deterministic like the rest, at a few samples.
        var small = settings with { SamplesPerPixel = 4, Atmosphere = AtmosphereMode.Reference };
        RenderOutput refA = Render(renderer, scene, small, aovs, new BatchPlannerOptions(), log, "reference atmosphere");
        RenderOutput refB = Render(renderer, scene, small, aovs, new BatchPlannerOptions(), log, "reference atmosphere again");
        RenderOutput refSplit = Render(renderer, scene, small, aovs, tight, log, "reference atmosphere, 0.05 ms budget");
        pass &= Check(log, "the reference atmosphere is sane", Sanity(refA, small));
        pass &= Check(log, "the reference atmosphere is bit-identical twice", Same(refA, refB, compareAovs: true));
        pass &= Check(log, "the reference atmosphere under a 0.05 ms budget is bit-identical", Same(refA, refSplit, compareAovs: true));

        // The reference with the terrain's shadow in the air (M1.4): deterministic too, and darker or equal on average.
        var shadowed = small with { Atmosphere = AtmosphereMode.ReferenceShadowed };
        RenderOutput shadA = Render(renderer, scene, shadowed, aovs, new BatchPlannerOptions(), log, "shadowed reference");
        RenderOutput shadB = Render(renderer, scene, shadowed, aovs, tight, log, "shadowed reference, 0.05 ms budget");
        pass &= Check(log, "the shadowed reference is sane", Sanity(shadA, shadowed));
        pass &= Check(log, "the shadowed reference under a 0.05 ms budget is bit-identical", Same(shadA, shadB, compareAovs: true));
        return pass & ValleyHazeChecks(renderer, scene, air, log);
    }

    /// <summary>
    /// The valley haze in the tables (M1.13a): the same parity with a strong layer under a lid 450 m above the ground sphere, so every
    /// table's march takes the layer's exact means over its steps as the C# reference does; then the renderer forgets that air.
    /// </summary>
    private static bool ValleyHazeChecks(D3D12VistaRenderer renderer, VistaScene scene, AtmosphereFrame clear, TextWriter log)
    {
        AtmosphereFrame air = clear with
        {
            Model = clear.Model with { ValleyHazeTopMetres = clear.GroundAltitudeMetres + 450.0, ValleyHazeExtinction550 = 2e-4 },
        };
        renderer.ForgetAtmosphere();
        AtmosphereLutData luts = renderer.BuildAtmosphere(air, scene.Wavelengths(), captureOrder2: true, out _);
        AtmosphereTables tables = AtmosphereTables.Build(air.Model, scene.Wavelengths());
        ScatteringOrder2 order2 = luts.Order2!;
        bool pass = Check(log, "with a valley haze, columns LUT texels match the C# reference", ColumnsParity(air, luts));
        pass &= Check(log, "with a valley haze, single-scattering texels match the C# reference", SingleParity(air, luts, order2, tables));
        pass &= Check(log, "with a valley haze, order-2 density texels match the C# reference", DensityParity(air, luts, order2, tables));
        pass &= Check(log, "with a valley haze, order-2 radiance texels match the C# reference", RadianceParity(air, order2, tables));
        pass &= Check(log, "with a valley haze, ground-irradiance entries match the C# reference", GroundParity(air, luts, tables));
        pass &= Check(log, "with a valley haze, sky-view LUT texels match the C# reference", SkyParity(air, luts, tables));
        pass &= Check(log, "with a valley haze, aerial perspective matches the C# reference", AerialParity(renderer, scene, air, luts, tables));
        renderer.ForgetAtmosphere();
        return pass;
    }

    /// <summary>
    /// What a watch session relies on (M1.8): a renderer that rebuilds only the part of the air a change touched renders the view
    /// exactly as one that rebuilds all of it. Three changes in a row on the same renderer — the time of day (the sky view and the sky
    /// distribution), a camera 400 m higher (the same; the mesh kept, as the LUTs are all this checks), the base altitude (the
    /// scattering orders too, and since M1.9 the mesh, bent onto the new ground sphere) — each rendered as the watch would, then again
    /// after <see cref="D3D12VistaRenderer.ForgetAtmosphere"/>; each pair must be bit-identical, and each render must report rebuilding
    /// only what its change touched. Since M1.12a two more: the engine's wavelengths and back to the bins (the wavelength tables and
    /// everything after them), and the engine's image must differ from the bins' (the quadrature reached the GPU).
    /// </summary>
    private static bool WatchChecks(D3D12VistaRenderer renderer, TerrainHierarchy hierarchy, VistaScene scene, RenderSettings settings, TextWriter log)
    {
        bool pass = true;
        var aovs = new HashSet<Aov>();
        Render(renderer, scene, settings, aovs, new BatchPlannerOptions(), log, "watch: the scene before the changes");
        VistaScene day = scene with { Sun = Almanac.For(scene.Recipe with { Time = TimeOfDay.Day }).Sun };
        VistaScene higher = day with { Camera = day.Camera with { Position = day.Camera.Position with { Y = day.Camera.Position.Y + 400.0 } } };
        VistaScene altitude = higher with { GroundAltitudeMetres = 3000.0, Mesh = MeshFor(hierarchy, scene.Camera, settings, 3000.0) };
        VistaScene engine = altitude with { Quadrature = Quadrature.Engine };
        VistaScene bins = engine with { Quadrature = WavelengthTable.DefaultQuadrature };
        string[] sky = [RenderStageKeys.SkyView, RenderStageKeys.SkyDistribution];
        string[] air = [RenderStageKeys.AtmosphereTables, RenderStageKeys.ScatteringOrders, .. sky];
        string[] keys = [RenderStageKeys.DeviceInit, RenderStageKeys.AccelerationBuild, RenderStageKeys.AtmosphereTables, RenderStageKeys.ScatteringOrders, .. sky];
        var warms = new Dictionary<string, RenderOutput>(StringComparer.Ordinal);
        foreach ((string label, VistaScene view, string[] rebuilds) in new[]
        {
            ("time of day", day, sky),
            ("camera 400 m higher", higher, sky),
            ("base altitude 3000 m", altitude, [RenderStageKeys.AccelerationBuild, RenderStageKeys.ScatteringOrders, .. sky]),
            ("engine's wavelengths", engine, air),
            ("default wavelengths again", bins, air),
        })
        {
            RenderOutput warm = Render(renderer, view, settings, aovs, new BatchPlannerOptions(), log, $"watch: {label}");
            warms[label] = warm;
            string[] built = [.. keys.Where(warm.Stats.StageSeconds.ContainsKey)];
            renderer.ForgetAtmosphere();
            RenderOutput cold = Render(renderer, view, settings, aovs, new BatchPlannerOptions(), log, $"watch: {label}, every LUT rebuilt");
            pass &= Check(log, $"watch: after the {label}, the partial rebuild renders as a full one",
                (built.SequenceEqual(keys.Where(rebuilds.Contains)) ? null : $"rebuilt {string.Join(", ", built)}")
                ?? Same(warm, cold, compareAovs: false));
        }
        pass &= Check(log, "watch: the engine's wavelengths change the image, and the default's bring it back",
            (Same(warms["base altitude 3000 m"], warms["engine's wavelengths"], compareAovs: false) is null ? "the same image" : null)
            ?? Same(warms["base altitude 3000 m"], warms["default wavelengths again"], compareAovs: false));
        return pass;
    }

    /// <summary>The view-dependent mesh (M1.9) of <paramref name="camera"/> on <paramref name="settings"/>' frame, bent onto the ground sphere at <paramref name="ground"/>.</summary>
    private static TerrainMesh MeshFor(TerrainHierarchy hierarchy, CameraSpec camera, RenderSettings settings, double ground) =>
        ViewMesher.Build(hierarchy, MeshView.For(camera, settings.Width, settings.Height, AtmosphereModel.Default.PlanetRadiusMetres + ground), new MeshSettings());

    /// <summary>
    /// The view-dependent mesh (M1.9) of the self-test's real world, on the CPU: built twice, the same bits; no crack (no vertex of
    /// the mesh at the midpoint of a triangle's edge, where a T-junction would open one); geometry behind the camera kept (shadows
    /// from off screen); and the far field there, past the world grid.
    /// </summary>
    private static bool MeshChecks(TerrainHierarchy hierarchy, VistaScene scene, RenderSettings settings, TextWriter log)
    {
        TerrainMesh mesh = scene.Mesh;
        MeshInfo info = mesh.Info;
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  hierarchy {hierarchy.BuildSeconds:0.00} s; mesh of {info.Triangles:N0} triangles ({info.FarTriangles:N0} far, {info.DetailTriangles:N0} detail), " +
            $"{info.Vertices:N0} vertices, {info.Bytes / 1048576.0:0.0} MiB, reach {info.ReachMetres / 1000.0:0} km, {info.RefineSeconds + info.VertexSeconds:0.00} s"));
        TerrainMesh again = MeshFor(hierarchy, scene.Camera, settings, scene.GroundAltitudeMetres);
        bool pass = Check(log, "the mesh built twice is bit-identical",
            mesh.Positions.AsSpan().SequenceEqual(again.Positions) && mesh.Normals.AsSpan().SequenceEqual(again.Normals) && mesh.Indices.AsSpan().SequenceEqual(again.Indices)
                ? null : "the two builds differ");
        var view = MeshView.For(scene.Camera, settings.Width, settings.Height, AtmosphereModel.Default.PlanetRadiusMetres + scene.GroundAltitudeMetres);
        int cracks = ViewMesher.TJunctions(hierarchy, view, new MeshSettings());
        pass &= Check(log, "the mesh has no crack (no vertex on another triangle's edge)", cracks == 0 ? null : $"{cracks} vertices sit on another triangle's edge");
        Vector3D forward = scene.Camera.Forward();
        int behind = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            uint v = mesh.Indices[3 * t];
            if (forward.X * mesh.Positions[3 * v] + forward.Z * mesh.Positions[3 * v + 2] < -1000.0)
                behind++;
        }
        pass &= Check(log, "the mesh keeps the ground behind the camera", behind > 0 ? null : "no triangle more than a kilometre behind the camera");
        pass &= Check(log, "the far field reaches past the world grid", info.FarTriangles > 0 && info.ReachMetres > 100_000.0 ? null
            : $"{info.FarTriangles} far triangles, reach {info.ReachMetres:0} m");
        return pass;
    }

    /// <summary>
    /// The GPU's aerial perspective (<c>CSAerialProbe</c>: the chord between frame points, its heights and verticals, the march)
    /// against <see cref="AerialPerspective"/> fed the GPU's own LUTs, on segments like the terrain's: down from the camera, a short
    /// bounce off to the side, a long grazing one across the world grid, one up a slope, and since M1.9 one to the far field 150 km
    /// away; at three wavelengths. The ends are world points placed in the camera's <see cref="EarthFrame"/>, as the mesh's are.
    /// </summary>
    private static string? AerialParity(D3D12VistaRenderer renderer, VistaScene scene, AtmosphereFrame air, AtmosphereLutData luts, AtmosphereTables tables)
    {
        Vector3D c = new(scene.Camera.Position.X, scene.Camera.Position.Y, scene.Camera.Position.Z);
        var segments = new (Vector3D A, Vector3D B)[]
        {
            (c, new Vector3D(c.X + 3000.0, 600.0, c.Z + 15_000.0)),
            (c, new Vector3D(c.X + 200.0, c.Y - 300.0, c.Z + 400.0)),
            (new Vector3D(c.X - 4000.0, 800.0, c.Z + 6000.0), new Vector3D(c.X - 3800.0, 950.0, c.Z + 6300.0)),
            (new Vector3D(c.X + 4000.0, 2400.0, c.Z + 4000.0), new Vector3D(c.X - 20_000.0, 10.0, c.Z + 20_000.0)),
            (new Vector3D(c.X, 520.0, c.Z + 2000.0), new Vector3D(c.X + 500.0, 2300.0, c.Z + 5000.0)),
            (c, new Vector3D(c.X + 120_000.0, 1800.0, c.Z + 90_000.0)),
        };
        var earth = new EarthFrame(air.GroundRadius, c.X, c.Z);
        Vector3 InFrame(Vector3D p)
        {
            Vector3D f = earth.ToFrame(p.X, p.Y, p.Z);
            return new Vector3((float)f.X, (float)f.Y, (float)f.Z);
        }
        var probes = new List<(Vector3 Start, Vector3 End, int Lambda)>();
        foreach ((Vector3D a, Vector3D b) in segments)
        {
            foreach (int lambda in ParityWavelengths)
                probes.Add((InFrame(a), InFrame(b), lambda));
        }
        float[] gpu = renderer.RunAerialProbe(air, scene.Wavelengths(), InFrame(c), probes);
        var worst = new Worst();
        var geometry = new Worst();
        for (int i = 0; i < probes.Count; i++)
        {
            (Vector3 a, Vector3 b, int lambda) = probes[i];
            AerialPerspective.Segment seg = AerialPerspective.Between(air, new Vector3D(a.X, a.Y, a.Z), new Vector3D(b.X, b.Y, b.Z));
            (double t, double s) = AerialPerspective.Integrate(luts.Columns, luts.Orders, air, tables.AsUploaded(lambda), tables.Sun[lambda * 4 + 3], seg);
            string where = string.Create(CultureInfo.InvariantCulture, $"segment {i / ParityWavelengths.Length} λ{lambda}");
            worst.Add($"{where} transmittance", gpu[i * 8], t, 1e-6);
            worst.Add($"{where} in-scattering", gpu[i * 8 + 1], s, 1e-9);
            geometry.Add($"{where} μ", gpu[i * 8 + 2], seg.Mu, 1e-2);
            geometry.Add($"{where} length", gpu[i * 8 + 3], seg.Length, 1.0);
            geometry.Add($"{where} height", gpu[i * 8 + 4], seg.Height, 1.0);
            geometry.Add($"{where} μ_s", gpu[i * 8 + 5], seg.MuS, 1e-2);
            geometry.Add($"{where} ν", gpu[i * 8 + 6], seg.Nu, 1e-2);
        }
        return geometry.Verdict() ?? worst.Verdict();
    }

    /// <summary>
    /// The sky as a light (M1.5): the distribution of this frame rebuilt is bit-identical; the GPU's luminance per (cell,
    /// slice) matches <see cref="SkyDistribution.CellLuminance"/> on the sky view <paramref name="luts"/> read back; the GPU's
    /// sampler and pdf match <see cref="SkyDistribution"/> on the same floats; and MIS agrees with M1.4's estimator on the
    /// image's mean.
    /// </summary>
    private static bool SkyLightChecks(D3D12VistaRenderer renderer, VistaScene scene, RenderSettings settings, AtmosphereLutData luts, TextWriter log)
    {
        bool pass = true;
        AtmosphereFrame air = scene.AirFrame();
        var small = settings with { Width = 160, Height = 90, SamplesPerPixel = 4 };
        Render(renderer, scene, small, new HashSet<Aov>(), new BatchPlannerOptions(), log, "MIS, sky distribution built");
        SkyLightData first = renderer.SkyLight!;
        renderer.ForgetAtmosphere();
        RenderOutput again = Render(renderer, scene, small, new HashSet<Aov>(), new BatchPlannerOptions(), log, "MIS, sky distribution rebuilt");
        SkyLightData rebuilt = renderer.SkyLight!;
        pass &= Check(log, "the sky distribution is rebuilt bit-identically",
            Differs(first.Luminance, rebuilt.Luminance) ?? Differs(first.Distribution.Floats, rebuilt.Distribution.Floats)
            ?? (rebuilt.Distribution.HasLight ? null : "the golden-hour sky has no light"));
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  sky distribution: {again.Stats.StageSeconds.GetValueOrDefault(RenderStageKeys.SkyDistribution) * 1e3:0.00} ms GPU, {again.Stats.StageSeconds.GetValueOrDefault("skyDistribution") * 1e3:0.0} ms in all"));

        AtmosphereTables atmosphere = AtmosphereTables.Build(air.Model, scene.Wavelengths());
        IntegratorTables integrator = IntegratorTables.Build(scene.Materials, scene.Wavelengths());
        var weights = new float[AtmosphereLuts.Wavelengths];
        for (int l = 0; l < weights.Length; l++)
            weights[l] = (float)SkyDistribution.LuminanceWeight(integrator, atmosphere, l);
        var worst = new Worst();
        foreach ((int band, int column) in new[] { (0, 0), (10, 63), (40, 5), (62, 0), (63, 1), (63, 40), (64, 0), (70, 31), (100, 12), (127, 63) })
        {
            foreach (int slice in new[] { 1, 6, 12 })
            {
                double expected = SkyDistribution.CellLuminance(luts.SkyView, air, weights, slice, band, column);
                float gpu = rebuilt.Luminance[(band * SkyDistribution.AzimuthBands + column) * AtmosphereLuts.SkyTerrainSlices + slice - 1];
                worst.Add(string.Create(CultureInfo.InvariantCulture, $"cell ({band}, {column}) slice {slice}"), gpu, expected, 1e-12);
            }
        }
        pass &= Check(log, "the sky's measured luminance matches the C# reference", worst.Verdict());
        pass &= Check(log, "the GPU's sky sampler and pdf match the C# reference", SkySamplingParity(renderer, rebuilt.Distribution, air));
        pass &= Check(log, "MIS and M1.4's estimator agree on the image's mean", Agreement(renderer, scene, small, log));
        return pass;
    }

    /// <summary>
    /// The incident meter (M1.10a): <c>CSIncidentMeter</c>'s band and sun items against <see cref="IncidentMeter.BandSky"/> and
    /// <see cref="IncidentMeter.SunOnCard"/> fed the LUTs <paramref name="luts"/> read back for <paramref name="scene"/>'s frame, on bands
    /// over the zenith, about the horizon and under it; the measurement made again after every LUT is rebuilt, the same bits; at golden
    /// hour the sun outshines the sky on the card, and in <paramref name="twilight"/> (the sun 4° down) the sun's term is under a
    /// millionth of the sky's, and the exposure is still finite.
    /// </summary>
    private static bool IncidentChecks(D3D12VistaRenderer renderer, VistaScene scene, VistaScene twilight, AtmosphereLutData luts, AtmosphereTables tables,
        TextWriter log)
    {
        AtmosphereFrame air = scene.AirFrame();
        IncidentLight golden = renderer.MeasureIncidentLight(scene);
        float[] raw = renderer.IncidentRaw!;
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  incident meter: {golden.StageSeconds.GetValueOrDefault(RenderStageKeys.IncidentMeter) * 1e3:0.00} ms GPU, {golden.StageSeconds.GetValueOrDefault("incidentMeter") * 1e3:0.0} ms in all"));
        var worst = new Worst();
        foreach (int lambda in ParityWavelengths)
        {
            int first = lambda / IncidentMeter.WavelengthsPerItem * IncidentMeter.ItemsPerGroup;
            int lane = lambda % IncidentMeter.WavelengthsPerItem;
            float toa = tables.Sun[lambda * 4 + 1];
            foreach (int band in new[] { 0, 20, 45, 63, 64, 66, 90, 127 })
            {
                double expected = IncidentMeter.BandSky(luts.SkyView, air, toa, lambda, band);
                worst.Add(string.Create(CultureInfo.InvariantCulture, $"band {band} λ{lambda}"), raw[(first + band) * 4 + lane], expected, 1e-6);
            }
            double sun = IncidentMeter.SunOnCard(luts.Columns, air, tables.AsUploaded(lambda), toa);
            worst.Add(string.Create(CultureInfo.InvariantCulture, $"sun λ{lambda}"), raw[(first + SkyDistribution.ZenithBands) * 4 + lane], sun, 1e-6);
        }
        bool pass = Check(log, "the incident meter's sky bands and sun match the C# reference", worst.Verdict());

        renderer.ForgetAtmosphere();
        IncidentLight again = renderer.MeasureIncidentLight(scene);
        pass &= Check(log, "the incident meter measured again on rebuilt LUTs is bit-identical",
            Differs(raw, renderer.IncidentRaw!) ?? (again.StageSeconds.ContainsKey(RenderStageKeys.SkyView) ? null : "the LUTs were not rebuilt"));

        IntegratorTables integrator = IntegratorTables.Build(scene.Materials, scene.Wavelengths());
        double sunY = IncidentMeter.Luminous(golden.Sun, integrator), skyY = IncidentMeter.Luminous(golden.Sky, integrator);
        IncidentLight dusk = renderer.MeasureIncidentLight(twilight);
        double duskSun = IncidentMeter.Luminous(dusk.Sun, integrator), duskSky = IncidentMeter.Luminous(dusk.Sky, integrator);
        ExposureInfo duskExposure = IncidentMeter.Measure(dusk, twilight.AirFrame(), integrator, 0.0);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  card at golden hour: sun {sunY * IntegratorTables.LuminancePerY:G4} lux, sky {skyY * IntegratorTables.LuminancePerY:G4} lux; " +
            $"at −4°: sun {duskSun:G3}, sky {duskSky * IntegratorTables.LuminancePerY:G4} lux, exposure {duskExposure.AppliedEv:+0.00;-0.00} EV"));
        pass &= Check(log, "at golden hour the sun outshines the sky on the card", sunY > skyY && skyY > 0.0 ? null : $"sun {sunY:G3}, sky {skyY:G3}");
        // Not exactly zero: SunVisibility's float asin leaves about 3·10⁻⁸ of a disk wholly under the horizon "visible" (the LUTs share it).
        pass &= Check(log, "4° after sunset the card is lit by the sky alone",
            duskSun < 1e-6 * duskSky && duskSky > 0.0 && duskExposure.MeteredEv is double ev && double.IsFinite(ev) ? null : $"sun {duskSun:G3}, sky {duskSky:G3}");
        return pass;
    }

    /// <summary>The GPU's <c>SampleSky</c>, <c>SkyPdf</c> and cell lookup against <see cref="SkyDistribution"/> on the same stored floats.</summary>
    private static string? SkySamplingParity(D3D12VistaRenderer renderer, SkyDistribution distribution, AtmosphereFrame air)
    {
        var random = new Random(42);
        var probes = new List<(Vector3 Random, Vector3 Direction)>();
        for (int i = 0; i < 4096; i++)
        {
            var u = new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle());
            double z = 2.0 * random.NextDouble() - 1.0, phi = 2.0 * Math.PI * random.NextDouble(), r = Math.Sqrt(1.0 - z * z);
            probes.Add((u, new Vector3((float)(r * Math.Cos(phi)), (float)z, (float)(r * Math.Sin(phi)))));
        }
        float[] gpu = renderer.RunSkyProbe(probes);
        var pdfs = new Worst();
        int directionMisses = 0, cellMisses = 0;
        double worstDirection = 0.0;
        for (int i = 0; i < probes.Count; i++)
        {
            (Vector3 u, Vector3 d) = probes[i];
            (Vector3D dir, double pdf) = distribution.Sample(u.X, u.Y, u.Z, air.ToSun);
            double angle = Math.Acos(Math.Clamp(dir.X * gpu[i * 8] + dir.Y * gpu[i * 8 + 1] + dir.Z * gpu[i * 8 + 2], -1.0, 1.0));
            worstDirection = Math.Max(worstDirection, angle);
            if (angle > 1e-3)
                directionMisses++;
            else
                pdfs.Add(string.Create(CultureInfo.InvariantCulture, $"sample {i}"), gpu[i * 8 + 3], pdf, 1e-12);
            Vector3D direction = new(d.X, d.Y, d.Z);
            (int band, int column) = distribution.CellOf(direction, air.ToSun);
            if ((int)gpu[i * 8 + 5] != band || (int)gpu[i * 8 + 6] != column)
                cellMisses++;
            else
                pdfs.Add(string.Create(CultureInfo.InvariantCulture, $"direction {i}"), gpu[i * 8 + 4], distribution.Pdf(direction, air.ToSun), 1e-12);
        }
        // Float trigonometry may put a direction a hair over a cell's edge; more than a few such is a real difference.
        if (directionMisses > 0 || cellMisses > probes.Count / 200)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{directionMisses} of {probes.Count} sampled directions differ (worst {worstDirection * 180.0 / Math.PI:G3}°), {cellMisses} directions land in another cell");
        }
        return pdfs.Verdict();
    }

    /// <summary>
    /// MIS against M1.4's estimator over independent renders (<see cref="EstimatorComparison"/>): the image's mean luminance
    /// within four standard errors.
    /// </summary>
    private static string? Agreement(D3D12VistaRenderer renderer, VistaScene scene, RenderSettings settings, TextWriter log)
    {
        const int replicates = 8;
        var mis = new ReplicateStats(settings.Width * settings.Height);
        var sunOnly = new ReplicateStats(settings.Width * settings.Height);
        RenderOutput? depth = null;
        for (int k = 0; k < replicates; k++)
        {
            var batching = new BatchPlannerOptions();
            RenderSettings a = settings with { SamplesPerPixel = 16, RandomStream = (uint)k + 1u };
            RenderOutput m = renderer.Render(scene, a, k == 0 ? new HashSet<Aov> { Aov.Depth } : new HashSet<Aov>(), new BatchPlanner(a.Width, a.Height, a.SamplesPerPixel, batching));
            depth ??= m;
            mis.Add(m.Xyz);
            RenderSettings b = a with { LightSampling = LightSampling.SunOnly, RandomStream = 1000u + (uint)k };
            sunOnly.Add(renderer.Render(scene, b, new HashSet<Aov>(), new BatchPlanner(b.Width, b.Height, b.SamplesPerPixel, batching)).Xyz);
        }
        EstimatorAgreement r = EstimatorComparison.Compare(mis, sunOnly, EstimatorComparison.InteriorTerrain(depth!.Aovs[Aov.Depth], settings.Width, settings.Height));
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  MIS against M1.4's estimator, {replicates} renders × 16 spp each: mean {r.MeanLuminanceError:+0.000%;-0.000%;0.000%} ± {r.MeanLuminanceNoise:0.000%}, " +
            $"RMS z {r.RmsZ:0.00}, terrain variance {r.VarianceReduction:0.00}× lower"));
        return Math.Abs(r.MeanLuminanceError) <= 4.0 * r.MeanLuminanceNoise
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"the means differ by {r.MeanLuminanceError:P3}, {Math.Abs(r.MeanLuminanceError) / r.MeanLuminanceNoise:0.0} standard errors");
    }

    private static string? ColumnsParity(AtmosphereFrame air, AtmosphereLutData luts)
    {
        var worst = new Worst();
        foreach (int j in new[] { 0, 1, 7, 31, 64, 128, 200, 255 })
        {
            foreach (int i in new[] { 0, 3, 64, 128, 200, 250, 255 })
            {
                DensityColumns c = AtmosphereLuts.ColumnsTexel(air.Model, i, j);
                int at = (j * AtmosphereLuts.ColumnsMuSize + i) * 4;
                worst.Add($"texel ({i}, {j}) Rayleigh", luts.Columns[at], c.Rayleigh, 1.0);
                worst.Add($"texel ({i}, {j}) haze", luts.Columns[at + 1], c.Aerosol, 1.0);
                worst.Add($"texel ({i}, {j}) ozone", luts.Columns[at + 2], c.Ozone, 1.0);
            }
        }
        return worst.Verdict();
    }

    /// <summary>
    /// 4D texels to spot-check, as fractions of each axis (so they follow the table sizes): every corner region of the
    /// domain, rays to the top (view fraction below ½) and to the ground (above ½).
    /// </summary>
    private static readonly (int H, int Mu, int MuS, int Nu)[] ScatterTexels =
    [
        .. new (double H, double Mu, double MuS, double Nu)[]
        {
            (0.0, 0.0, 1.0, 1.0), (0.0, 0.49, 0.5, 0.4), (0.0, 0.5, 0.62, 0.0), (0.0, 1.0, 1.0, 1.0), (0.2, 0.16, 0.16, 0.57),
            (0.33, 0.63, 0.77, 0.86), (0.5, 0.31, 0.1, 0.14), (0.67, 0.78, 0.9, 0.29), (1.0, 0.0, 0.0, 1.0), (1.0, 0.49, 0.58, 0.71),
            (0.47, 0.52, 0.35, 1.0), (0.13, 0.94, 0.39, 0.0),
        }.Select(f => (
            (int)Math.Round(f.H * (AtmosphereScattering.HeightSize - 1)),
            (int)Math.Round(f.Mu * (AtmosphereScattering.MuSize - 1)),
            (int)Math.Round(f.MuS * (AtmosphereScattering.MuSSize - 1)),
            (int)Math.Round(f.Nu * (AtmosphereScattering.NuSize - 1)))),
    ];

    /// <summary>Hero wavelengths spot-checked in the per-render tables (blue, green, red).</summary>
    private static readonly int[] ParityWavelengths = [0, 27, 49];

    /// <summary>Table wavelengths spot-checked in the 4D tables (<see cref="AtmosphereScattering.HeroOf"/>: hero 0, 25 and 49).</summary>
    private static readonly int[] ParityTableWavelengths = [0, 5, 10];

    private static string? IrradianceParity(AtmosphereFrame air, AtmosphereLutData luts, ScatteringOrder2 order2, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach (int entry in new[] { 0, 60, 64, 70, 100, 127 })
        {
            foreach (int l in ParityTableWavelengths)
            {
                int at = entry * AtmosphereScattering.MsWavelengths + l;
                worst.Add($"direct, entry {entry} l{l}", order2.Direct[at],
                    AtmosphereScattering.DirectIrradianceEntry(luts.Columns, air, tables.AsUploaded(AtmosphereScattering.HeroOf(l)), entry), 1e-9);
                worst.Add($"order 1, entry {entry} l{l}", order2.Irradiance[at],
                    AtmosphereScattering.IndirectIrradianceEntry(order2.Single, [], firstOrder: true, air, l, entry), 1e-9);
            }
        }
        return worst.Verdict();
    }

    private static string? SingleParity(AtmosphereFrame air, AtmosphereLutData luts, ScatteringOrder2 order2, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach ((int h, int mu, int mus, int nu) in ScatterTexels)
        {
            foreach (int l in ParityTableWavelengths)
            {
                (double rayleigh, double mie) = AtmosphereScattering.SingleScatteringTexel(luts.Columns, air, tables.AsUploaded(AtmosphereScattering.HeroOf(l)), h, mu, mus, nu);
                int at = AtmosphereScattering.Index(h, mu, mus, nu, l, 2);
                worst.Add($"texel ({h}, {mu}, {mus}, {nu}) l{l} Rayleigh", order2.Single[at], rayleigh, 1e-9);
                worst.Add($"texel ({h}, {mu}, {mus}, {nu}) l{l} haze", order2.Single[at + 1], mie, 1e-9);
            }
        }
        return worst.Verdict();
    }

    private static string? DensityParity(AtmosphereFrame air, AtmosphereLutData luts, ScatteringOrder2 order2, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach ((int h, int mu, int mus, int nu) in ScatterTexels)
        {
            foreach (int l in ParityTableWavelengths)
            {
                (double rayleigh, double mie) = AtmosphereScattering.DensityTexel(luts.Columns, order2.Single, [], order2.Direct, air,
                    tables.AsUploaded(AtmosphereScattering.HeroOf(l)), 2, l, h, mu, mus, nu);
                int at = AtmosphereScattering.Index(h, mu, mus, nu, l, 2);
                worst.Add($"texel ({h}, {mu}, {mus}, {nu}) l{l} Rayleigh", order2.Density[at], rayleigh, 1e-9);
                worst.Add($"texel ({h}, {mu}, {mus}, {nu}) l{l} haze", order2.Density[at + 1], mie, 1e-9);
            }
        }
        return worst.Verdict();
    }

    private static string? RadianceParity(AtmosphereFrame air, ScatteringOrder2 order2, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach ((int h, int mu, int mus, int nu) in ScatterTexels)
        {
            foreach (int l in ParityTableWavelengths)
            {
                double expected = AtmosphereScattering.MultipleScatteringTexel(order2.Density, air, tables.AsUploaded(AtmosphereScattering.HeroOf(l)), l, h, mu, mus, nu);
                worst.Add($"texel ({h}, {mu}, {mus}, {nu}) l{l}", order2.Radiance[AtmosphereScattering.Index(h, mu, mus, nu, l, 1)], expected, 1e-9);
            }
        }
        return worst.Verdict();
    }

    private static string? GroundParity(AtmosphereFrame air, AtmosphereLutData luts, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach (int entry in new[] { 50, 64, 70, 100, 127 })
        {
            foreach (int lambda in new[] { 0, 49 })
            {
                double expected = AtmosphereLuts.GroundIrradianceEntry(luts.Columns, luts.Orders, air, tables.AsUploaded(lambda), lambda, tables.Sun[lambda * 4 + 3],
                    entry);
                worst.Add($"entry {entry} λ{lambda}", luts.GroundIrradiance[entry * AtmosphereLuts.Wavelengths + lambda], expected, 1e-12);
            }
        }
        return worst.Verdict();
    }

    private static string? SkyParity(AtmosphereFrame air, AtmosphereLutData luts, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach ((int slice, int row, int column) in new[]
        {
            (0, 0, 0), (0, 20, 5), (0, 63, 0), (0, 63, 63), (0, 64, 10), (0, 80, 40), (0, 95, 63),
            (1, 40, 30), (6, 62, 1), (12, 66, 33), (12, 10, 60),
        })
        {
            foreach (int lambda in new[] { 0, 27, 49 })
            {
                (double smooth, double mie) = AtmosphereLuts.SkyViewTexel(luts.Columns, luts.Orders, luts.GroundIrradiance, air,
                    tables.AsUploaded(lambda), lambda, tables.Sun[lambda * 4 + 3], slice, row, column);
                int at = ((((slice * AtmosphereLuts.SkyRows) + row) * AtmosphereLuts.SkyAzimuthSize + column) * AtmosphereLuts.Wavelengths + lambda) * 2;
                worst.Add($"texel ({slice}, {row}, {column}) λ{lambda} smooth", luts.SkyView[at], smooth, 1e-12);
                worst.Add($"texel ({slice}, {row}, {column}) λ{lambda} haze", luts.SkyView[at + 1], mie, 1e-12);
            }
        }
        return worst.Verdict();
    }

    /// <summary>Tracks the largest relative difference of GPU texels from their references.</summary>
    private sealed class Worst
    {
        private double _error;
        private string _where = string.Empty;
        private int _count;

        public void Add(string where, float gpu, double expected, double floor)
        {
            _count++;
            double error = float.IsFinite(gpu) ? Math.Abs(gpu - expected) / Math.Max(Math.Abs(expected), floor) : double.PositiveInfinity;
            if (error > _error || double.IsNaN(error))
                (_error, _where) = (error, string.Create(CultureInfo.InvariantCulture, $"{where}: GPU {gpu:G6}, C# {expected:G6}"));
        }

        public string? Verdict() => _error <= LutTolerance
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{_count} values, worst relative difference {_error:G3} at {_where}");
    }

    private static string? Twilight(RenderOutput output)
    {
        double sumY = 0.0;
        for (int p = 0; p < output.Xyz.Length; p += 3)
        {
            if (!float.IsFinite(output.Xyz[p]) || !float.IsFinite(output.Xyz[p + 1]) || !float.IsFinite(output.Xyz[p + 2]))
                return $"pixel {p / 3} is not finite";
            sumY += output.Xyz[p + 1];
        }
        return sumY > 0.0 ? null : "the twilight image is black";
    }

    private static RenderOutput Render(D3D12VistaRenderer renderer, VistaScene scene, RenderSettings settings,
        IReadOnlySet<Aov> aovs, BatchPlannerOptions batching, TextWriter log, string label)
    {
        var planner = new BatchPlanner(settings.Rendered.Width, settings.Rendered.Height, settings.SamplesPerPixel, batching);
        RenderOutput output = renderer.Render(scene, settings with { Batching = batching }, aovs, planner);
        RenderStats s = output.Stats;
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {label}: {s.Dispatches} dispatches, {s.RaysTraced:N0} rays ({s.RaysPerSample():0.00}/sample), " +
            $"{s.TraceSeconds * 1e3:0.0} ms GPU, longest dispatch {s.LongestDispatchSeconds * 1e3:0.00} ms"));
        return output;
    }

    /// <summary>
    /// The estimate's video memory (M1.15): a fresh renderer (a device of its own, so its base holds what the process holds now) plans
    /// <paramref name="scene"/> at <paramref name="settings"/> with <paramref name="aovs"/> before anything of it exists, then renders it;
    /// the peak it measured must lie within <see cref="MemoryPlan.Margin"/> of the plan's.
    /// </summary>
    private static bool MemoryChecks(VistaScene scene, RenderSettings settings, IReadOnlySet<Aov> aovs, TextWriter log)
    {
        using var fresh = new D3D12VistaRenderer();
        MemoryPlan plan = fresh.PlanMemory(scene, settings, aovs);
        RenderOutput output = Render(fresh, scene, settings, aovs, new BatchPlannerOptions(), log, "a fresh renderer's frame");
        long peak = output.Stats.PeakVideoMemoryBytes;
        string numbers = string.Create(CultureInfo.InvariantCulture,
            $"planned {plan.PeakBytes / 1048576.0:0.0} MiB at its peak ({plan.PeakStage}), measured {peak / 1048576.0:0.0} MiB");
        log.WriteLine($"  {numbers}");
        return Check(log, "the planned video memory meets the measured peak", Math.Abs(peak - plan.PeakBytes) <= MemoryPlan.Margin * plan.PeakBytes ? null : numbers);
    }

    internal static bool Check(TextWriter log, string name, string? failure)
    {
        log.WriteLine(failure is null ? $"[ok]   {name}" : $"[FAIL] {name}: {failure}");
        return failure is null;
    }

    private static string? Sanity(RenderOutput output, RenderSettings settings)
    {
        double sumY = 0.0;
        for (int p = 0; p < output.Xyz.Length; p += 3)
        {
            if (!float.IsFinite(output.Xyz[p]) || !float.IsFinite(output.Xyz[p + 1]) || !float.IsFinite(output.Xyz[p + 2]))
                return $"pixel {p / 3} is not finite";
            sumY += output.Xyz[p + 1];
        }
        if (sumY <= 0.0)
            return "the image is black";
        float[] ids = output.Aovs[Aov.MaterialId];
        int sky = ids.Count(id => id < 0f);
        if (sky == 0 || sky == ids.Length)
            return $"{sky} of {ids.Length} pixels see sky; expected both sky and terrain in frame";
        long pixelSamples = (long)settings.Width * settings.Height * settings.SamplesPerPixel;
        if (output.Stats.PixelSamples != pixelSamples)
            return $"{output.Stats.PixelSamples} pixel-samples traced, expected {pixelSamples}";
        if (output.Stats.RaysTraced <= (ulong)pixelSamples)
            return $"{output.Stats.RaysTraced} rays for {pixelSamples} pixel-samples; the ray counter is not counting";
        return null;
    }

    private static string? Mixed(RenderOutput output)
    {
        float[] ids = output.Aovs[Aov.MaterialId];
        int sky = ids.Count(id => id < 0f);
        return sky > 0 && sky < ids.Length ? null : $"{sky} of {ids.Length} pixels see sky; move the window onto the horizon";
    }

    private static string? SameAsCrop(RenderOutput frame, RenderOutput loupe, PixelWindow window)
    {
        if (loupe.Width != window.Width || loupe.Height != window.Height)
            return $"the loupe is {loupe.Width}×{loupe.Height}, expected {window.Width}×{window.Height}";
        if (Differs(window.Crop(frame.Xyz, frame.Width, 3), loupe.Xyz) is { } xyz)
            return $"XYZ {xyz}";
        foreach ((Aov aov, float[] data) in frame.Aovs)
        {
            if (Differs(window.Crop(data, frame.Width, AovLayout.Channels(aov)), loupe.Aovs[aov]) is { } diff)
                return $"{aov} {diff}";
        }
        return null;
    }

    private static string? Same(RenderOutput a, RenderOutput b, bool compareAovs)
    {
        if (Differs(a.Xyz, b.Xyz) is { } xyz)
            return $"XYZ {xyz}";
        if (a.Stats.RaysTraced != b.Stats.RaysTraced)
            return $"ray counts differ ({a.Stats.RaysTraced} vs {b.Stats.RaysTraced})";
        if (!compareAovs)
            return null;
        foreach ((Aov aov, float[] data) in a.Aovs)
        {
            if (Differs(data, b.Aovs[aov]) is { } diff)
                return $"{aov} {diff}";
        }
        return null;
    }

    private static string? Differs(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return $"lengths differ ({a.Length} vs {b.Length})";
        if (MemoryMarshal.AsBytes(a.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(b.AsSpan())))
            return null;
        int count = 0;
        double worst = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            if (BitConverter.SingleToInt32Bits(a[i]) == BitConverter.SingleToInt32Bits(b[i]))
                continue;
            count++;
            worst = Math.Max(worst, Math.Abs((double)a[i] - b[i]));
        }
        return string.Create(CultureInfo.InvariantCulture, $"differs in {count} of {a.Length} floats (max |Δ| {worst:G4})");
    }
}
