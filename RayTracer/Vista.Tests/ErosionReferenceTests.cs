using Vista.Recipes;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The C# reference of the GPU erosion (vista-plan §10 M1.7 verify: "the C# reference conserves water and sediment within
/// tolerance, never yields NaN, and has a pinned hash"). The GPU runs the same passes; <c>--gpu-selftest</c> checks it against
/// this reference on the RTX 3070.
/// </summary>
[TestClass]
public sealed class ErosionReferenceTests
{
    private static readonly WorldGrid Patch = new(65, 8.0);

    private static HeightField Mountains(ulong seed = 42) =>
        TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.AlpineRange] }, seed, Patch);

    private static HeightField Field(int size, double cell, Func<int, int, float> height)
    {
        var heights = new float[size * size];
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
                heights[j * size + i] = height(i, j);
        }
        return new HeightField(size, cell, heights);
    }

    [TestMethod]
    public void Water_IsConserved_ButForRainEvaporationAndTheEdges()
    {
        // Per step: Σ(water after the flow) = Σ(water before + rain) − what left over the edges, and evaporation then takes its
        // fixed fraction. Checked step by step from bare ground, through the wet-up and into the steady flow.
        var state = new ErosionState(Mountains());
        ErosionConstants k = ErosionConstants.For(ErosionSettings.Mountains, Patch.Size, Patch.CellSize).WithStrata(state);
        int n = Patch.Size, cells = n * n;
        var terrainB = new float[cells * 2];
        var waterB = new float[cells];
        var sedimentB = new float[cells];
        double worst = 0.0;
        for (int step = 0; step < 300; step++)
        {
            ErosionReference.Flux(k, state.Terrain, state.Water, state.Flux);
            ErosionReference.SpreadDischarge(k, state);
            double before = state.Water.Sum(w => (double)w + k.Rain);
            double leftOverEdges = EdgeOutflow(n, state.Flux, (c, flux) => flux);
            ErosionReference.Water(k, state.Terrain, state.Water, state.Sediment, state.Flux, terrainB, waterB, sedimentB, state.Flow, state.Strata, state.Bands, state.Discharge);
            double after = waterB.Sum(w => (double)w) / k.Retain;
            worst = Math.Max(worst, Math.Abs(after - (before - k.DepthPerFlux * leftOverEdges)) / before);
            ErosionReference.Transport(k, state.Water, state.Flux, sedimentB, state.Sediment);
            waterB.CopyTo(state.Water, 0);
            ErosionReference.ThermalOut(k, terrainB, state.Thermal, state.Strata, state.Bands);
            ErosionReference.ThermalApply(k, terrainB, state.Thermal, state.Terrain);
        }
        Assert.IsTrue(worst < 1e-5, $"water out of balance by {worst:E2} of the total in one step");
        Assert.IsTrue(state.Water.Sum() > 0f, "rain has wetted the patch");
    }

    [TestMethod]
    public void Material_IsConserved_ButForTheSedimentTheWaterCarriesOverTheEdges()
    {
        // Bedrock + loose + suspended sediment changes only by the sediment that leaves over the edges with the water.
        var state = new ErosionState(Mountains());
        ErosionConstants k = ErosionConstants.For(ErosionSettings.Mountains, Patch.Size, Patch.CellSize).WithStrata(state);
        int n = Patch.Size, cells = n * n;
        var terrainB = new float[cells * 2];
        var waterB = new float[cells];
        var sedimentB = new float[cells];
        double total = Total(state.Terrain, state.Sediment), lost = 0.0, eroded = 0.0;
        for (int step = 0; step < 300; step++)
        {
            ErosionReference.Flux(k, state.Terrain, state.Water, state.Flux);
            ErosionReference.SpreadDischarge(k, state);
            ErosionReference.Water(k, state.Terrain, state.Water, state.Sediment, state.Flux, terrainB, waterB, sedimentB, state.Flow, state.Strata, state.Bands, state.Discharge);
            float[] water = state.Water;
            lost += EdgeOutflow(n, state.Flux, (c, flux) => sedimentB[c] * (flux * (k.DepthPerFlux / (water[c] + k.Rain))));
            ErosionReference.Transport(k, state.Water, state.Flux, sedimentB, state.Sediment);
            waterB.CopyTo(state.Water, 0);
            ErosionReference.ThermalOut(k, terrainB, state.Thermal, state.Strata, state.Bands);
            ErosionReference.ThermalApply(k, terrainB, state.Thermal, state.Terrain);
            eroded = Math.Max(eroded, state.Sediment.Sum(s => (double)s));
        }
        double now = Total(state.Terrain, state.Sediment);
        double error = Math.Abs(now - (total - lost));
        Assert.IsTrue(eroded > 1.0, $"the water carried sediment ({eroded:0.00} m summed over the cells at most)");
        Assert.IsTrue(error < 1e-6 * total, $"material out of balance by {error:0.000} m summed over the cells (of {total:0} m, {lost:0.00} m lost over the edges)");
    }

    [TestMethod]
    public void Thermal_ConservesTheGround_AndBringsLooseSlopesToTheTalusAngle()
    {
        // A cone of loose material on flat bedrock, far steeper than 35°, slides down to it.
        const int n = 33;
        const double cell = 8.0;
        HeightField flat = Field(n, cell, (_, _) => 100f);
        var state = new ErosionState(flat);
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                double r = Math.Sqrt((i - 16) * (i - 16) + (j - 16) * (j - 16));
                state.Terrain[(j * n + i) * 2 + 1] = (float)Math.Max(0.0, 120.0 - 20.0 * r);
            }
        }
        ErosionConstants k = ErosionConstants.For(ErosionSettings.Mountains, n, cell);
        double before = Total(state.Terrain, state.Sediment);
        var other = new float[n * n * 2];
        for (int step = 0; step < 3000; step++)
        {
            ErosionReference.ThermalOut(k, state.Terrain, state.Thermal, state.Strata, state.Bands);
            ErosionReference.ThermalApply(k, state.Terrain, state.Thermal, other);
            other.CopyTo(state.Terrain, 0);
        }
        double after = Total(state.Terrain, state.Sediment);
        Assert.AreEqual(before, after, 1e-6 * before, "the ground is conserved");
        double steepest = SteepestEdgeDrop(n, state.Terrain) / cell;
        Assert.IsTrue(steepest <= Math.Tan(35.0 * Math.PI / 180.0) * 1.05, $"the pile settled at {Math.Atan(steepest) * 180 / Math.PI:0.0}°");
        Assert.IsTrue(state.Terrain.Where((v, i) => i % 2 == 0).All(rock => rock == 100f), "the bedrock below did not move");
    }

    [TestMethod]
    public void Thermal_CollapsesRockSteeperThanItsTalus_AndLeavesTheRestStanding()
    {
        // A rock wall of 70° collapses to the rock talus (50°), its debris lands as loose material; a 40° rock slope stands. Without
        // strata every rock is the softest, whose talus this is.
        const int n = 33;
        const double cell = 8.0;
        double tan70 = Math.Tan(70.0 * Math.PI / 180.0), tan40 = Math.Tan(40.0 * Math.PI / 180.0);
        var wall = new ErosionState(Field(n, cell, (i, _) => (float)(Math.Clamp(i - 12, 0, 8) * cell * tan70)));
        var slope = new ErosionState(Field(n, cell, (i, _) => (float)(Math.Clamp(i - 12, 0, 8) * cell * tan40)));
        // M1.7's thermal rate, so 2000 steps settle the wall (the shipped mountains' slower rate, M1a.2, takes longer to the same talus).
        ErosionConstants k = ErosionConstants.For(ErosionSettings.Mountains with { RockTalusDegrees = 50f, RockTalusHardDegrees = 60f, ThermalRate = 0.25f }, n, cell)
            .WithStrata(wall);
        foreach (ErosionState state in new[] { wall, slope })
        {
            var other = new float[n * n * 2];
            for (int step = 0; step < 2000; step++)
            {
                ErosionReference.ThermalOut(k, state.Terrain, state.Thermal, state.Strata, state.Bands);
                ErosionReference.ThermalApply(k, state.Terrain, state.Thermal, other);
                other.CopyTo(state.Terrain, 0);
            }
        }
        // The ground, not the rock: the debris buries the foot of the wall, which may stay steep beneath it.
        double steepest = SteepestEdgeDrop(n, wall.Terrain) / cell;
        Assert.IsTrue(steepest <= Math.Tan(50.0 * Math.PI / 180.0) * 1.05, $"the wall stands at {Math.Atan(steepest) * 180 / Math.PI:0.0}°");
        Assert.IsTrue(wall.Terrain.Where((v, i) => i % 2 == 1).Sum() > 100f, "the collapsed rock lies as loose material");
        Assert.IsTrue(slope.Terrain.Where((v, i) => i % 2 == 1).All(loose => loose == 0f), "rock below its talus does not move");
    }

    [TestMethod]
    public void Water_RunsDownhill_AndLeavesOverTheLowEdge()
    {
        // A plane falling to the west: the flow gathers downhill, and the water drains over the low edge instead of pooling.
        const int n = 33;
        HeightField plane = Field(n, 8.0, (i, _) => 4f * i);
        ErosionResult result = ErosionReference.Erode(plane, ErosionSettings.Mountains with { Iterations = 400, AverageFrom = 200 });
        double low = Enumerable.Range(1, n - 2).Average(j => result.Discharge[j * n + 1]);
        double high = Enumerable.Range(1, n - 2).Average(j => result.Discharge[j * n + n - 2]);
        Assert.IsTrue(low > 5 * high, $"discharge {low:0.000} m³/s downhill against {high:0.000} uphill");
        Assert.IsTrue(result.StandingWater.Max() < 1f, $"the water drained (deepest {result.StandingWater.Max():0.00} m)");
    }

    [TestMethod]
    public void TheReference_NeverYieldsNaN_OrNegativeWaterSedimentOrLoose()
    {
        const int n = 33;
        var fields = new Dictionary<string, HeightField>
        {
            ["flat"] = Field(n, 8.0, (_, _) => 0f),
            ["spike"] = Field(n, 8.0, (i, j) => i == 16 && j == 16 ? 500f : 0f),
            ["cliff"] = Field(n, 8.0, (i, _) => i < 16 ? 0f : 1000f),
            ["noise"] = Field(n, 8.0, (i, j) => (float)(StableHash.Lattice(9, i, j) % 600) - 300f),
            ["pit"] = Field(n, 8.0, (i, j) => i == 16 && j == 16 ? -400f : 0f),
        };
        foreach (ErosionSettings settings in new[]
        {
            ErosionSettings.Mountains with { Iterations = 300, AverageFrom = 100 },
            ErosionSettings.Mountains with { Iterations = 300, RainPerStep = 0.5f, FluxDamping = 0f, Dissolve = 1f, Deposition = 1f },
            ErosionSettings.Hills with { Iterations = 300, RainPerStep = 0f },
        })
        {
            foreach ((string name, HeightField field) in fields)
            {
                var state = new ErosionState(field);
                ErosionReference.Run(state, settings, settings.Iterations);
                foreach ((string what, float[] values, bool signed) in new[] { ("terrain", state.Terrain, true), ("water", state.Water, false), ("sediment", state.Sediment, false), ("flux", state.Flux, false), ("flow", state.Flow, false) })
                {
                    Assert.IsTrue(values.All(float.IsFinite), $"{name}: {what} is not finite");
                    if (!signed)
                        Assert.IsTrue(values.All(v => v >= 0f), $"{name}: {what} went negative");
                }
                Assert.IsTrue(state.Terrain.Where((v, i) => i % 2 == 1).All(loose => loose >= 0f), $"{name}: loose went negative");
            }
        }
    }

    [TestMethod]
    public void TheReference_IsDeterministic_AndPinned()
    {
        // Change-detector: if this moves, every eroded world moves (and the cache's PassesVersion must change with it). M1.7's
        // passes (no spread of the discharge, M1.7's thermal rate) keep their pin; M1a.2's have their own.
        foreach ((ErosionSettings erosion, string surface, string result) in new[]
        {
            (ErosionSettings.Mountains with { DischargeSpread = 0, ThermalRate = 0.25f }, PinnedSurfaceHash, PinnedResultHash),
            (ErosionSettings.Mountains, PinnedSpreadSurfaceHash, PinnedSpreadResultHash),
        })
        {
            ErosionSettings settings = erosion with { Iterations = 200, AverageFrom = 100 };
            ErosionResult a = ErosionReference.Erode(Mountains(), settings);
            ErosionResult b = ErosionReference.Erode(Mountains(), settings);
            Assert.AreEqual(a.ContentHash(), b.ContentHash());
            Assert.AreEqual(surface, a.Surface.ContentHash().ToString("x16"), $"got {a.Surface.ContentHash():x16}");
            Assert.AreEqual(result, a.ContentHash().ToString("x16"), $"got {a.ContentHash():x16}");
        }
    }

    private const string PinnedSurfaceHash = "05c7ac89357f294b";
    private const string PinnedResultHash = "3f6a3639797cffa9";
    private const string PinnedSpreadSurfaceHash = "78b10ec152f08795";
    private const string PinnedSpreadResultHash = "5d0f3f3f7f21466c";

    [TestMethod]
    public void SpreadingTheDischarge_SpreadsALaneOverAChannelsWidth_AndKeepsItsTotal()
    {
        // Two spreads of [1 2 1] / 4 are [1 4 6 4 1] / 16 along each axis: a lane of discharge one cell wide becomes five wide.
        const int n = 17;
        ErosionConstants k = ErosionConstants.For(ErosionSettings.Mountains with { DischargeSpread = 2 }, n, 8.0);
        Assert.AreEqual(2u, k.SpreadDischarge);
        var discharge = new float[n * n];
        for (int j = 0; j < n; j++)
            discharge[j * n + 8] = 1f;
        var scratch = new float[n * n];
        for (int p = 0; p < 2; p++)
        {
            ErosionReference.SpreadAlongX(k, discharge, scratch);
            ErosionReference.SpreadAlongZ(k, scratch, discharge);
        }
        float[] expected = [1f / 16f, 4f / 16f, 6f / 16f, 4f / 16f, 1f / 16f];
        for (int d = -2; d <= 2; d++)
            Assert.AreEqual(expected[d + 2], discharge[8 * n + 8 + d], 1e-6f, $"{d} cells from the lane");
        Assert.AreEqual(0f, discharge[8 * n + 5]);
        Assert.AreEqual(n, discharge.Sum(), 1e-4f, "the total is kept");
        Assert.AreEqual(0u, ErosionConstants.For(ErosionSettings.Mountains with { DischargeSpread = 0 }, n, 8.0).SpreadDischarge);
    }

    [TestMethod]
    public void SpreadingTheDischarge_TurnsTheCombOnAFaceFallingAlongTheGrid_IntoGullies()
    {
        // A face falling 10° off the grid's rows, roughened by half a metre. Without the spread each lane of cells carries its own
        // water, which no water crosses, and cuts its own rill: a comb two to four cells apart, along the rows (vista-plan §10
        // M1a.2). With the discharge spread over a channel's width the water cuts gullies that follow the fall line, wider apart.
        const int n = 129;
        const double cell = 8.0;
        double turn = 10.0 * Math.PI / 180.0, slope = Math.Tan(35.0 * Math.PI / 180.0);
        HeightField face = Field(n, cell, (i, j) =>
            (float)(3000.0 - slope * cell * (i * Math.Cos(turn) + j * Math.Sin(turn)) + (StableHash.Lattice(7, i, j) % 1000) / 1000.0 - 0.5));
        GridArtefacts Eroded(int spread)
        {
            ErosionResult r = ErosionReference.Erode(face, ErosionSettings.Mountains with { Iterations = 300, PoolFillMetres = 0f, DischargeSpread = spread });
            return GridArtefacts.Measure(r.Surface, c => c % n is > 16 and < n - 16 && c / n is > 16 and < n - 16);
        }
        GridArtefacts lanes = Eroded(0), spread = Eroded(2);
        Assert.IsTrue(lanes.RippleShare > 0.5, $"without the spread a comb: {lanes.RippleShare:0.0%} of the windows ripple");
        Assert.IsTrue(lanes.AlignedShare > 0.4, $"along the grid's rows: {lanes.AlignedShare:0.0%} of the relief");
        Assert.IsTrue(spread.RippleShare < 0.1, $"with it no comb: {spread.RippleShare:0.0%} of the windows ripple");
        Assert.IsTrue(spread.AlignedShare < 0.25, $"and the gullies follow the fall line: {spread.AlignedShare:0.0%} of the relief along the grid");
    }

    [TestMethod]
    public void Erosion_CarvesTheMountains_AndLeavesLooseMaterialLowerDown()
    {
        HeightField ground = Mountains(7);
        ErosionResult result = ErosionReference.Erode(ground, ErosionSettings.Mountains with { Iterations = 400, AverageFrom = 200 });
        double moved = Enumerable.Range(0, ground.Heights.Length).Average(c => Math.Abs(result.Surface.Heights[c] - ground.Heights[c]));
        Assert.IsTrue(moved > 0.5, $"the ground moved {moved:0.00} m on average");
        float[] curvature = TerrainMaps.Curvature(result.Surface);
        double looseInHollows = Enumerable.Range(0, curvature.Length).Where(c => curvature[c] > 0f).Average(c => result.Loose[c]);
        double looseOnRidges = Enumerable.Range(0, curvature.Length).Where(c => curvature[c] < 0f).Average(c => result.Loose[c]);
        Assert.IsTrue(looseInHollows > looseOnRidges, $"loose material: {looseInHollows:0.00} m in hollows against {looseOnRidges:0.00} m on ridges");
    }

    [TestMethod]
    public void Result_SettlesTheSuspendedSediment_AndRoundsTheMapsToHalves()
    {
        var state = new ErosionState(Field(3, 8.0, (_, _) => 1000f));
        state.Sediment[4] = 0.25f;
        state.Terrain[4 * 2 + 1] = 1.1f;
        state.Flow[4 * 2] = 30.000001f;
        state.Flow[4 * 2 + 1] = 3f;
        state.Water[4] = 0.123456789f;
        state.FlowSteps = 3;
        ErosionResult result = ErosionResult.From(state);
        Assert.AreEqual(1000f + (1.1f + 0.25f), result.Surface[1, 1], "the sediment settles on the loose material");
        Assert.AreEqual((float)(Half)(1.1f + 0.25f), result.Loose[4]);
        Assert.AreEqual((float)(Half)(30.000001f / 3f), result.Discharge[4], "the flow sums become means");
        Assert.AreEqual(1f, result.WaterDepth[4]);
        Assert.AreEqual((float)(Half)0.123456789f, result.StandingWater[4]);
        Assert.AreEqual(1000f, result.Surface[0, 0]);
    }

    [TestMethod]
    public void Result_FillsThePools_UpToTheLimit()
    {
        // A plane falling to the west with two holes: 3 m and 30 m deep below their west rims. With a 10 m limit the shallow one
        // fills to its rim, a flat the water crosses, and the deep one rises by 10 m and stays a lake; its water is gone either way.
        const int n = 17;
        var state = new ErosionState(Field(n, 8.0, (i, j) => 2f * i - (i == 5 && j == 5 ? 5f : 0f) - (i == 11 && j == 11 ? 32f : 0f)));
        state.Water[5 * n + 5] = 1f;
        ErosionResult kept = ErosionResult.From(state);
        ErosionResult filled = ErosionResult.From(state, poolFillMetres: 10f);
        Assert.AreEqual(2f * 5 - 5f, kept.Surface[5, 5], "without the fill the hole stays");
        Assert.AreEqual(2f * 4, filled.Surface[5, 5], "the shallow hole fills to its rim");
        Assert.AreEqual(3f, filled.Loose[5 * n + 5], "the fill is loose material");
        Assert.AreEqual(0f, filled.StandingWater[5 * n + 5], "its water is gone");
        Assert.AreEqual(2f * 11 - 32f + 10f, filled.Surface[11, 11], 1e-4f, "the deep one rises by the limit");
        Assert.IsTrue(filled.Drainage.FillDepth[11 * n + 11] > 1f, "and still holds a lake");
        Assert.AreEqual(0f, filled.Drainage.FillDepth[5 * n + 5], "the filled one drains");
        Assert.AreEqual(kept.Surface[8, 8], filled.Surface[8, 8], "the plane does not move");
    }

    [TestMethod]
    public void Curvature_IsTheLaplacian()
    {
        // h = a(x² + z²) has Laplacian 4a everywhere; the edges are left at 0.
        const double a = 0.001;
        HeightField bowl = Field(17, 10.0, (i, j) => (float)(a * ((i - 8) * 10.0 * (i - 8) * 10.0 + (j - 8) * 10.0 * (j - 8) * 10.0)));
        float[] curvature = TerrainMaps.Curvature(bowl);
        Assert.AreEqual(4 * a, curvature[8 * 17 + 8], 1e-6);
        Assert.AreEqual(4 * a, curvature[3 * 17 + 12], 1e-6);
        Assert.AreEqual(0f, curvature[0]);
    }

    [TestMethod]
    public void Settings_Validate_Hash_AndOverride()
    {
        Assert.AreEqual(0, ErosionSettings.Mountains.Validate().Count);
        Assert.AreEqual(0, ErosionSettings.Hills.Validate().Count);
        Assert.AreEqual(1, (ErosionSettings.Mountains with { ThermalRate = 0.6f }).Validate().Count);
        Assert.AreEqual(1, (ErosionSettings.Mountains with { RockTalusDegrees = 30f }).Validate().Count);
        Assert.AreEqual(1, (ErosionSettings.Mountains with { TimeStep = 0f }).Validate().Count);
        Assert.AreEqual(1, (ErosionSettings.Mountains with { Iterations = -1 }).Validate().Count);
        Assert.AreEqual(1, (ErosionSettings.Mountains with { IncisionSteps = -1 }).Validate().Count);
        Assert.AreEqual(1, (ErosionSettings.Mountains with { IncisionTalusDegrees = 90f }).Validate().Count);
        Assert.AreEqual(1, (ErosionSettings.Mountains with { IncisionLakeFill = 1.5f }).Validate().Count);
        Assert.AreEqual(1, (ErosionSettings.Mountains with { RefineCubic = 2 }).Validate().Count);
        Assert.AreEqual(1, (ErosionSettings.Mountains with { RefineFaceDetail = 1.5f }).Validate().Count);
        Assert.ThrowsExactly<ArgumentException>(() => ErosionReference.Erode(Mountains(), ErosionSettings.Mountains with { Dissolve = 2f }));

        Assert.AreSame(ErosionSettings.Mountains, ErosionSettings.For(TerrainProfile.Mountains));
        Assert.AreSame(ErosionSettings.Hills, ErosionSettings.For(TerrainProfile.Hills));
        Assert.AreNotEqual(ErosionSettings.Mountains.ContentHash(), ErosionSettings.Hills.ContentHash());
        Assert.AreNotEqual(ErosionSettings.Mountains.ContentHash(), (ErosionSettings.Mountains with { AverageFrom = 1 }).ContentHash());
        Assert.AreNotEqual(ErosionSettings.Mountains.ContentHash(), (ErosionSettings.Mountains with { FluxDamping = 0.5f }).ContentHash());
        Assert.AreNotEqual(ErosionSettings.Mountains.ContentHash(), (ErosionSettings.Mountains with { IncisionSteps = 301 }).ContentHash());
        Assert.AreNotEqual(ErosionSettings.Mountains.ContentHash(), (ErosionSettings.Mountains with { IncisionUplift = 3f }).ContentHash());
        Assert.AreNotEqual(ErosionSettings.Mountains.ContentHash(), (ErosionSettings.Mountains with { IncisionLakeFill = 0.1f }).ContentHash());
        Assert.AreNotEqual(ErosionSettings.Mountains.ContentHash(), (ErosionSettings.Mountains with { RefineFaceDetail = 0.25f }).ContentHash());
        Assert.AreNotEqual(ErosionSettings.Mountains.ContentHash(), (ErosionSettings.Mountains with { RefineChannelCurves = 1 }).ContentHash());

        ErosionSettings tuned = ErosionSettings.Mountains.With("Capacity=3; Iterations=10,DischargeExponent=0.5");
        Assert.AreEqual((3f, 10, 0.5f), (tuned.Capacity, tuned.Iterations, tuned.DischargeExponent));
        Assert.AreEqual(2f, ErosionSettings.Mountains.Capacity, "overrides make a copy");
        Assert.ThrowsExactly<ArgumentException>(() => ErosionSettings.Mountains.With("Capacty=3"));
    }

    [TestMethod]
    public void Constants_FitTheShadersCbuffer_AndDeriveFromTheSettings()
    {
        Assert.AreEqual(128, System.Runtime.CompilerServices.Unsafe.SizeOf<ErosionConstants>(), "eight 16-byte rows, as VistaErosion.hlsl declares");
        ErosionSettings settings = ErosionSettings.Mountains with { RockTalusDegrees = 40f, RockTalusHardDegrees = 65f, HardErodibility = 0.25f };
        ErosionConstants k = ErosionConstants.For(settings, 65, 8.0);
        Assert.AreEqual(0.25f * 20f * 9.81f / 8f, k.FluxPerHead);
        Assert.AreEqual(0.25f / 64f, k.DepthPerFlux);
        Assert.AreEqual(1f - 0.8f, k.FluxKept);
        Assert.AreEqual(Math.Tan(35.0 * Math.PI / 180.0) * 8.0 * Math.Sqrt(2.0), k.LooseDropCorner, 1e-4);
        Assert.AreEqual(0.01f, k.HalfThermalRate);
        Assert.AreEqual(2u, k.SpreadDischarge, "M1a.2's spread discharge");
        Assert.AreEqual(Math.Tan(40.0 * Math.PI / 180.0) * 8.0, k.RockDropEdge, 1e-4);
        Assert.AreEqual(Math.Tan(65.0 * Math.PI / 180.0) * 8.0 * Math.Sqrt(2.0), k.RockDropCornerHard, 1e-4);
        Assert.AreEqual(settings.RockErodibility * 0.25f, k.RockErodibilityHard);
        Assert.AreEqual(0f, k.TableScale, "the bed table's come from the state");
        var state = new ErosionState(TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.AlpineRange] }, 1, new WorldGrid(9, 8.0)),
            StrataGrid.For(RockHardness.For(1, settings), 9, 8.0, new StrataFrame(0.0, 0.0, 2.0, 0), (_, _) => 0.0));
        k = k.WithStrata(state);
        Assert.AreEqual(1f / (2f * RockHardness.TableSpacingMetres), k.TableScale);
        Assert.AreEqual(RockHardness.TableEntries - 1.001f, k.TableLast);
    }

    // Σ over the edge cells of what each sends past the edge: f(c, its outflow toward the outside).
    private static double EdgeOutflow(int n, float[] flux, Func<int, float, double> f)
    {
        double sum = 0.0;
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int c = j * n + i;
                if (i == 0) sum += f(c, flux[c * 4]);
                if (i == n - 1) sum += f(c, flux[c * 4 + 1]);
                if (j == 0) sum += f(c, flux[c * 4 + 2]);
                if (j == n - 1) sum += f(c, flux[c * 4 + 3]);
            }
        }
        return sum;
    }

    private static double Total(float[] terrain, float[] sediment) => terrain.Sum(v => (double)v) + sediment.Sum(v => (double)v);

    private static double SteepestEdgeDrop(int n, float[] terrain)
    {
        double steepest = 0.0;
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int c = j * n + i;
                double h = terrain[c * 2] + terrain[c * 2 + 1];
                if (i < n - 1) steepest = Math.Max(steepest, Math.Abs(h - (terrain[(c + 1) * 2] + terrain[(c + 1) * 2 + 1])));
                if (j < n - 1) steepest = Math.Max(steepest, Math.Abs(h - (terrain[(c + n) * 2] + terrain[(c + n) * 2 + 1])));
            }
        }
        return steepest;
    }
}
