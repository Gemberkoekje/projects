using System.Globalization;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.Sliders;
using Vista.Weather;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>The valleys' pools on the GPU against their C# references (vista-plan §10 M1.13b).</summary>
internal static partial class GpuSelfTest
{
    /// <summary>The flag that runs the low air's checks alone, on the cached seed-42 world.</summary>
    public const string LowAirFlag = "--low-air-check";

    /// <summary>
    /// The low air's checks alone (<see cref="LowAirFlag"/>), for work on the pools: the seed-42 world from the erosion cache (eroded
    /// once on this GPU if the cache lacks it), meshed for the self-test's camera.
    /// </summary>
    public static int RunLowAir(TextWriter log)
    {
        var recipe = new Recipe { Name = "gpu self-test", SetPieces = [SetPiece.SnowCappedMountains], Time = TimeOfDay.GoldenHour };
        const ulong seed = 42;
        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}, driver {renderer.DriverVersion}");
        TerrainWorld world = WorldBuilder.Build(recipe, seed, WorldGrid.Default, renderer, ErosionCache.Default);
        ErosionResult eroded = world.Erosion ?? throw new InvalidOperationException("The self-test's world was not eroded.");
        Almanac almanac = Almanac.For(recipe);
        double ground = Places.BaseAltitude(recipe, almanac.Place).Metres;
        var settings = new RenderSettings { Width = 320, Height = 180, SamplesPerPixel = 16, MaxBounces = 6 };
        TerrainHierarchy hierarchy = TerrainHierarchy.Build(eroded.Surface, TerrainProfile.For(recipe.SetPieces), seed, TerrainDetail.None);
        TerrainMesh mesh = MeshFor(hierarchy, SelfTestCamera, settings, ground);
        var scene = new VistaScene(recipe, seed, SelfTestCamera, eroded.Surface, mesh, almanac.Sun, MaterialCatalog.Ground, AtmosphereModel.Default, ground);
        bool pass = LowAirChecks(renderer, recipe, seed, eroded, scene, settings, log);
        log.WriteLine(pass ? "PASS" : "FAIL");
        return pass ? 0 : 1;
    }

    /// <summary>
    /// The pools of an October morning at Zermatt over the self-test's world with a strong valley haze and mist pinned, so every part
    /// of the density is there: the GPU's pool lookup, noise and density at places around the camera against <see cref="LowAir"/>'s; the
    /// sunlight volume's columns toward the sun and up against <see cref="LowAir.VoxelColumns"/>, and its terrain shadow against a
    /// march over the height field (the GPU's is the bent mesh, so the two agree on most voxels, not all); the render with pools sane
    /// and bit-identical twice, under a tight budget too, and the reference with pools sane.
    /// </summary>
    private static bool LowAirChecks(D3D12VistaRenderer renderer, Recipe recipe, ulong seed, ErosionResult eroded, VistaScene scene, RenderSettings settings,
        TextWriter log)
    {
        Recipe october = recipe with { Location = Place.Zermatt, Date = new DateOnly(2026, 10, 15), Time = TimeOfDay.Day };
        SliderPins pins = SliderPins.FromRecipe(october) with
        {
            SolarTime = new TimeOnly(7, 30),
            Altitude = scene.GroundAltitudeMetres,
            ValleyHaze = 6.0,
            Mist = 0.4,
        };
        SliderState sliders = LinkedSliders.Resolve(october, pins, AtmosphereModel.Default, WorldFooting.Of(eroded.Surface));
        PoolMap map = PoolMap.For(eroded.Surface, eroded.Drainage, PoolMap.FarTerrain(recipe, seed, WorldGrid.Default));
        ValleyPools pools = ValleyPools.Build(map, sliders.Pools, sliders.BaseAltitudeMetres);
        AtmosphereModel air = sliders.Air(AtmosphereModel.Default, LowAir.TablesLayer(pools));
        LowAir? low = LowAir.For(pools, air, seed);
        if (low is null)
            return Check(log, "the pinned morning has pools", "no pools");
        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  pools: {pools.Stats.Describe()}; their reach {low.SlabTop:0} m"));
        VistaScene misty = scene with { Air = air, Sun = sliders.Almanac.Sun, LowAir = low };

        // The pool, the noise and the density at places around the camera, from under the floor to above the top.
        var places = new List<(float X, float Altitude, float Z)>();
        for (int j = 0; j < 24; j++)
        {
            for (int i = 0; i < 24; i++)
            {
                float x = (float)(scene.Camera.Position.X - 6000.0 + 521.7 * i), z = (float)(scene.Camera.Position.Z - 6000.0 + 517.3 * j);
                PoolSample s = low.PoolAt(x, z, out _);
                foreach (float share in new[] { -0.1f, 0.05f, 0.3f, 0.6f, 0.9f, 1.05f, 1.3f })
                    places.Add((x, s.Floor + share * Math.Max(s.Depth, 50f), z));
            }
        }
        float[] gpu = renderer.RunLowAirProbe(misty, places);
        double worstPool = 0.0, worstDensity = 0.0, worstNoise = 0.0;
        int dense = 0, misted = 0;
        for (int k = 0; k < places.Count; k++)
        {
            (float x, float altitude, float z) = places[k];
            PoolSample s = low.PoolAt(x, z, out _);
            (float haze, float mist) = low.Density(s, x, z, altitude);
            float wisp = LowAir.Noise3(low.NoiseSeed, x / (float)low.Settings.WispScaleMetres, altitude / (float)low.Settings.WispVerticalMetres,
                z / (float)low.Settings.WispScaleMetres);
            float patch = LowAir.Noise2(MaterialNoise.Hash(low.NoiseSeed ^ 0x9E3779B9u), x / (float)low.Settings.PatchScaleMetres, z / (float)low.Settings.PatchScaleMetres);
            worstPool = Math.Max(worstPool, Math.Max(Math.Abs(gpu[k * 16] - s.Floor) / Math.Max(Math.Abs(s.Floor), 1f),
                Math.Max(Math.Abs(gpu[k * 16 + 1] - s.Depth), Math.Abs(gpu[k * 16 + 2] - s.Mist)) / 100.0));
            worstDensity = Math.Max(worstDensity, Math.Max(Math.Abs(gpu[k * 16 + 4] - haze) / (low.HazeMax + 1e-9),
                Math.Abs(gpu[k * 16 + 5] - mist) / (low.MistMax + 1e-9)));
            worstNoise = Math.Max(worstNoise, Math.Max(Math.Abs(gpu[k * 16 + 6] - wisp), Math.Abs(gpu[k * 16 + 7] - patch)));
            dense += haze > 0.1f * low.HazeMax ? 1 : 0;
            misted += mist > 0f ? 1 : 0;
        }
        bool pass = Check(log, "the shader's pool map lookup matches the C# reference", worstPool <= 1e-4 ? null
            : string.Create(CultureInfo.InvariantCulture, $"worst {worstPool:G3} (relative floor, depths in hundreds of metres)"));
        pass &= Check(log, "the shader's noise matches the C# reference", worstNoise <= 1e-4 ? null
            : string.Create(CultureInfo.InvariantCulture, $"worst {worstNoise:G3}"));
        pass &= Check(log, "the shader's pool density matches the C# reference", worstDensity <= 2e-3 ? null
            : string.Create(CultureInfo.InvariantCulture, $"worst {worstDensity:G3} of the densest"));
        pass &= Check(log, "the parity check reaches dense haze and mist", dense >= places.Count / 20 && misted >= places.Count / 20 ? null
            : $"{dense} places in dense haze and {misted} in mist of {places.Count}");

        // The sunlight volume: its columns against the C# reference (the sun at each voxel as the shader takes it), at voxels around the
        // camera and where the mist lies; its shadow against a march over the height field, near the camera (where the Earth's curve
        // is a metre, which the flat march leaves out).
        float[] volume = renderer.ReadSunlightVolume(misty, 0);
        int size = low.VolumeSize(low.Level0), layers = low.Settings.VolumeLayers;
        float cell = (float)(low.Level0.CellMetres * low.Settings.VolumeRatio);
        int ci = (int)Math.Round((scene.Camera.Position.X - low.Level0.OriginX) / cell), cj = (int)Math.Round((scene.Camera.Position.Z - low.Level0.OriginZ) / cell);
        var sun = sliders.Almanac.Sun.ToSun;
        double horizontal = Math.Sqrt(sun.X * sun.X + sun.Z * sun.Z);
        var columns = new List<(int I, int J, bool Near)>();
        for (int j = Math.Max(cj - 20, 0); j <= Math.Min(cj + 20, size - 1); j += 4)
        {
            for (int i = Math.Max(ci - 20, 0); i <= Math.Min(ci + 20, size - 1); i += 4)
                columns.Add((i, j, true));
        }
        int misty0 = 0;
        for (int k = 0; k < size * size && misty0 < 120; k += 97)
        {
            int i = k % size, j = k / size;
            PoolSample s = low.PoolAt((float)(low.Level0.OriginX + i * cell), (float)(low.Level0.OriginZ + j * cell), out _);
            if (s.Mist > 20f)
            {
                columns.Add((i, j, false));
                misty0++;
            }
        }
        double worstColumn = 0.0;
        string worstAt = "";
        int agree = 0, compared = 0, shadowed = 0, toward = 0, voxels = 0;
        foreach ((int i, int j, bool near) in columns)
        {
            for (int layer = 0; layer < layers; layer += 3)
            {
                int v = (layer * size + j) * size + i;
                float x = (float)(low.Level0.OriginX + i * cell), z = (float)(low.Level0.OriginZ + j * cell);
                PoolSample s = low.PoolAt(x, z, out _);
                float altitude = s.Floor + low.LayerHeight(s, layer);
                var local = LowAir.LocalToSun(scene.Mesh.Frame, x, altitude - scene.GroundAltitudeMetres, z, ((float)sun.X, (float)sun.Y, (float)sun.Z));
                (float hazeDelta, float mistDepth, float mistUp, _, _, _) = low.VoxelColumns(0, i, j, layer, local);
                // The haze column is the pools' less the tables' layer's, two near-equal columns toward a low sun: its error scales with them.
                double layerColumn = local.Y > 0f ? air.ValleyHazeColumn(altitude, low.SlabTop) / local.Y : 0.0;
                double error = Math.Max(Math.Abs(volume[v * 4 + 1] - hazeDelta) / (1.0 + 0.01 * layerColumn + 0.01 * Math.Abs(hazeDelta)),
                    Math.Max(Math.Abs(volume[v * 4 + 2] - mistDepth) / (0.1 + 0.01 * mistDepth), Math.Abs(volume[v * 4 + 3] - mistUp) / (0.1 + 0.01 * mistUp)));
                if (error > worstColumn)
                    worstAt = string.Create(CultureInfo.InvariantCulture,
                        $"voxel ({i}, {j}, {layer}) at {altitude:0} m: GPU ({volume[v * 4 + 1]:G4}, {volume[v * 4 + 2]:G4}, {volume[v * 4 + 3]:G4}), C# ({hazeDelta:G4}, {mistDepth:G4}, {mistUp:G4})");
                worstColumn = Math.Max(worstColumn, error);
                toward += mistDepth > 0.5f ? 1 : 0;
                voxels++;
                if (!near)
                    continue;
                bool lit = LowAir.TerrainLit(eroded.Surface, scene.GroundAltitudeMetres, x, altitude, z, sun.X / horizontal, sun.Z / horizontal, sun.Y);
                bool gpuLit = volume[v * 4] > 0.5f;
                agree += lit == gpuLit ? 1 : 0;
                shadowed += gpuLit ? 0 : 1;
                compared++;
            }
        }
        pass &= Check(log, "the sunlight volume's columns toward the sun and up match the C# reference", worstColumn <= 1.0 ? null
            : string.Create(CultureInfo.InvariantCulture, $"worst {worstColumn:G3} of its tolerance, {worstAt}"));
        pass &= Check(log, "the parity check reaches mist toward the sun", toward >= voxels / 20 ? null : $"{toward} of {voxels} voxels");
        pass &= Check(log, "the sunlight volume's terrain shadow agrees with the height field's on most voxels",
            agree >= 0.85 * compared && shadowed > 0 && shadowed < compared ? null
            : string.Create(CultureInfo.InvariantCulture, $"{agree} of {compared} agree, {shadowed} shadowed"));
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  the volume: {voxels} voxels' columns within {worstColumn:0.00} of their tolerance, {toward} with mist toward the sun; its shadow agrees with the height field's on {agree} of {compared}, {shadowed} shadowed"));

        // Renders with the pools: sane, the same twice and under a tight budget; the reference with them sane.
        var small = settings with { Width = 160, Height = 90, SamplesPerPixel = 8 };
        var allAovs = new HashSet<Aov>(Enum.GetValues<Aov>());
        var tight = new BatchPlannerOptions { DispatchBudgetMs = 0.05, InitialCostNs = 20000 };
        RenderOutput a = Render(renderer, misty, small, allAovs, new BatchPlannerOptions(), log, "with pools");
        RenderOutput b = Render(renderer, misty, small, allAovs, new BatchPlannerOptions(), log, "with pools, again");
        RenderOutput c = Render(renderer, misty, small, allAovs, tight, log, "with pools, 0.05 ms budget");
        pass &= Check(log, "the render with pools is sane", Sanity(a, small));
        pass &= Check(log, "the render with pools is bit-identical twice", Same(a, b, compareAovs: true));
        pass &= Check(log, "the render with pools under a 0.05 ms budget is bit-identical", Same(a, c, compareAovs: true));
        RenderOutput plain = Render(renderer, scene with { Air = air, Sun = sliders.Almanac.Sun }, small, allAovs, new BatchPlannerOptions(), log, "without pools");
        pass &= Check(log, "the pools change the image", Same(a, plain, compareAovs: false) is null ? "the same image with and without pools" : null);
        RenderOutput reference = Render(renderer, misty, small with { Atmosphere = AtmosphereMode.Reference, SamplesPerPixel = 4 }, allAovs,
            new BatchPlannerOptions(), log, "reference with pools");
        pass &= Check(log, "the reference with pools is sane", Sanity(reference, small with { SamplesPerPixel = 4 }));
        return pass;
    }
}
