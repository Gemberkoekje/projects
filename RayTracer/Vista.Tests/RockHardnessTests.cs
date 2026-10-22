using Vista.Recipes;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The uplift map and the rock's hardness (vista-plan §10 M1.11d verify: "both fields are pure functions of seed and position;
/// hardness moves erodibility and talus angle as specified (synthetic tests); the incision still drains every basin").
/// </summary>
[TestClass]
public sealed class RockHardnessTests
{
    private static readonly Recipe Alpine = new() { SetPieces = [SetPiece.AlpineRange] };

    // Hardness that matters: the hardest rock a tenth as erodible as the softest and standing much steeper.
    private static readonly ErosionSettings Rock = ErosionSettings.Mountains with
    {
        HardErodibility = 0.1f,
        IncisionTalusDegrees = 30f,
        IncisionTalusHardDegrees = 60f,
        RockTalusDegrees = 40f,
        RockTalusHardDegrees = 65f,
    };

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
    public void Hardness_IsAPureFunctionOfSeedAndPosition_AndPinned()
    {
        RockHardness a = RockHardness.For(42, ErosionSettings.Mountains), b = RockHardness.For(42, ErosionSettings.Mountains);
        Assert.AreEqual(a.ContentHash(), b.ContentHash());
        Assert.AreNotEqual(a.ContentHash(), RockHardness.For(43, ErosionSettings.Mountains).ContentHash(), "another seed, other beds");
        foreach ((double x, double y, double z) in new[] { (0.0, 1200.0, 0.0), (-9000.5, 300.25, 15000.0), (16000.0, 2400.0, -16000.0) })
        {
            Assert.AreEqual(a.At(x, y, z), b.At(x, y, z));
            Assert.IsTrue(a.At(x, y, z) is >= 0f and <= 1f);
        }
        Assert.AreEqual(PinnedHardnessHash, a.ContentHash().ToString("x16"));
        Assert.AreEqual(PinnedHardnessAt, a.At(1234.5, 1500.0, -2345.5), 1e-6f);
    }

    private const string PinnedHardnessHash = "1ac705dc0e62475c";
    private const float PinnedHardnessAt = 0.2487916f;

    [TestMethod]
    public void TheBeds_AreHardOrSoft_AndAsThickAsTheSettingsSay()
    {
        RockHardness rock = RockHardness.For(7, ErosionSettings.Mountains with { StrataThickness = 40f });
        // A bed is a run of equal entries; its thickness is a quarter to two and a half times the mean.
        var runs = new List<double>();
        int start = 0;
        for (int k = 1; k < rock.Table.Length; k++)
        {
            if (rock.Table[k] != rock.Table[k - 1])
            {
                if (start > 0)
                    runs.Add((k - start) * RockHardness.TableSpacingMetres);
                start = k;
            }
        }
        Assert.IsTrue(runs.Count > 500, $"{runs.Count} beds in the table");
        Assert.IsTrue(runs.Min() >= 0.25 * 40 - RockHardness.TableSpacingMetres && runs.Max() <= 2.5 * 40 + RockHardness.TableSpacingMetres,
            $"beds from {runs.Min()} to {runs.Max()} m");
        Assert.AreEqual(40.0, runs.Average(), 4.0, "the mean bed");
        Assert.IsTrue(rock.Table.All(v => v is >= 0f and <= 1f));
        Assert.IsTrue(rock.Table.Count(v => v < 0.2f) > rock.Table.Length / 10 && rock.Table.Count(v => v > 0.8f) > rock.Table.Length / 10,
            "soft and hard beds both common");
    }

    [TestMethod]
    public void TheBeds_FollowTheDip_AndTheMottleStaysWithinItsAmplitude()
    {
        ErosionSettings flat = ErosionSettings.Mountains with { StrataDipDegrees = 0f, HardnessMottle = 0f };
        RockHardness level = RockHardness.For(5, flat);
        // Without dip, mottle or fold amplitude beyond its own, a bed's height varies only by the fold's slow warp.
        for (double x = -10000; x <= 10000; x += 2500)
            Assert.IsTrue(Math.Abs(level.Fold(x, 0) - level.Fold(0, 0)) <= 240.0, $"fold at {x}");
        RockHardness tilted = RockHardness.For(5, ErosionSettings.Mountains with { StrataDipDegrees = 20f, HardnessMottle = 0f });
        double spread = 0;
        for (double x = -10000; x <= 10000; x += 2500)
            spread = Math.Max(spread, Math.Abs(tilted.Fold(x, 0) - tilted.Fold(-x, 0)));
        Assert.IsTrue(spread <= 2 * 10000 * Math.Tan(20 * Math.PI / 180) + 480, "the dip is at most the settings'");
        RockHardness mottled = RockHardness.For(5, ErosionSettings.Mountains with { HardnessMottle = 0.2f });
        for (double x = -16000; x <= 16000; x += 777)
            Assert.IsTrue(Math.Abs(mottled.Mottle(x, 0.37 * x)) <= 0.2 * 1.0001, $"mottle at {x}");
    }

    [TestMethod]
    public void TheGridLayout_AgreesWithTheField_ThroughTheFrame()
    {
        RockHardness rock = RockHardness.For(11, ErosionSettings.Mountains);
        var frame = new StrataFrame(Low: 400.0, Bottom: 650.0, Scale: 1.25, UpliftSteps: 300);
        static double Uplift(double x, double z) => 2.0 + 0.0001 * x;
        const int n = 33;
        const double cell = 64.0;
        StrataGrid grid = StrataGrid.For(rock, n, cell, frame, Uplift);
        double origin = -0.5 * (n - 1) * cell;
        int mismatched = 0;
        for (int j = 0; j < n; j += 4)
        {
            for (int i = 0; i < n; i += 4)
            {
                double x = origin + i * cell, z = origin + j * cell;
                foreach (float y in new[] { 500f, 1333.5f, 2400f })
                {
                    float expected = rock.At(x, frame.RockHeight(y, Uplift(x, z)), z);
                    if (Math.Abs(grid.At(j * n + i, y) - expected) > 2e-3f)
                        mismatched++;
                }
            }
        }
        // Rounded to float in another order, a lookup that lands within a rounding of a bed's edge may read its neighbour.
        Assert.IsTrue(mismatched <= 2, $"{mismatched} of {9 * 9 * 3} lookups differ");
        Assert.AreEqual(1200.0, StrataFrame.Identity.RockHeight(1200.0, 5.0), "without an incision the frames are one");
    }

    [TestMethod]
    public void TheShape_IsTheTerrainsOwnLowOctaves()
    {
        TerrainProfile mountains = TerrainProfile.Mountains;
        ulong seed = SeedDerivation.Derive(42, SeedDerivation.TerrainBase);
        foreach ((double x, double z) in new[] { (0.0, 0.0), (-7000.0, 3100.0), (15000.0, -15000.0) })
        {
            Assert.AreEqual(mountains.Height(seed, x, z), mountains.Shape(seed, x, z, mountains.Octaves) * mountains.ReliefMetres,
                "with every octave the shape is the terrain over its relief");
            double low = mountains.Shape(seed, x, z, 1);
            Assert.IsTrue(low is >= 0.0 and <= 1.0);
        }
    }

    [TestMethod]
    public void TheUpliftMap_IsAPureFunction_ZeroOnTheEdge_AndHighWhereTheMassifsAre()
    {
        ErosionSettings settings = ErosionSettings.Mountains with { UpliftOctaves = 2, UpliftContrast = 2f, IncisionUplift = 4f };
        HeightField ground = TerrainGenerator.Generate(Alpine, 42, TestWorlds.Grid);
        float[] a = UpliftMap.On(Alpine, 42, settings, ground);
        float[] b = UpliftMap.On(Alpine, 42, settings, ground);
        CollectionAssert.AreEqual(a, b);
        CollectionAssert.AreNotEqual(a, UpliftMap.On(Alpine, 43, settings, ground));
        for (int k = 0; k < 65; k++)
        {
            Assert.AreEqual(0f, a[k]);
            Assert.AreEqual(0f, a[64 * 65 + k]);
            Assert.AreEqual(0f, a[k * 65]);
            Assert.AreEqual(0f, a[k * 65 + 64]);
        }
        Assert.IsTrue(a.All(u => u is >= 0f and <= 4f));
        // The map follows the base terrain's two lowest octaves, squared: where they are high, the uplift is.
        ulong seed = SeedDerivation.Derive(42, SeedDerivation.TerrainBase);
        double origin = -0.5 * 64 * 512.0;
        for (int c = 0; c < a.Length; c += 97)
        {
            int i = c % 65, j = c / 65;
            if (i == 0 || j == 0 || i == 64 || j == 64)
                continue;
            double shape = TerrainProfile.Mountains.Shape(seed, origin + i * 512.0, origin + j * 512.0, 2);
            Assert.AreEqual((float)(4.0 * shape * shape), a[c]);
        }
        Assert.AreEqual(a[30 * 65 + 31], (float)UpliftMap.At(Alpine, 42, settings, origin + 31 * 512.0, origin + 30 * 512.0));
    }

    [TestMethod]
    public void HardRock_IncisesMoreSlowly()
    {
        // A valley falling to the edge at x = 0 (its floor on j = 16), gentler than the talus angle (one for both rocks here, so
        // only the erodibility differs): with the same water, the soft floor cuts down faster than the hard one. Neither run moves
        // the highest corner or the outlet, so neither is stretched when it is mapped back to the valley's range.
        const int n = 33;
        HeightField valley = Field(n, 64.0, (i, j) => (float)(30.0 * i + 25.0 * Math.Abs(j - 16)));
        var none = new float[n * n];
        ErosionSettings settings = Rock with { IncisionSteps = 10, IncisionTalusDegrees = 45f, IncisionTalusHardDegrees = 45f };
        (HeightField soft, StrataFrame softFrame) = StreamPower.Incise(valley, settings, none, (_, _) => 0f);
        HeightField hard = StreamPower.Incise(valley, settings, none, (_, _) => 1f).Incised;
        Assert.AreEqual(1.0, softFrame.Scale, "not stretched");
        double Cut(HeightField f) => valley[16, 16] - f[16, 16];
        Assert.IsTrue(Cut(soft) > Cut(hard) + 1.0, $"the soft floor cut {Cut(soft):0.0} m at mid-length, the hard one {Cut(hard):0.0} m");
        Assert.IsTrue(Cut(hard) > 0.0, "the hard floor still cuts");
        double neutral = Cut(StreamPower.Incise(valley, settings with { HardErodibility = 1f }, none, (_, _) => 1f).Incised);
        Assert.AreEqual(Cut(soft), neutral, "with HardErodibility 1 and one talus angle hardness changes nothing");
    }

    [TestMethod]
    public void HardRock_StandsSteeperInTheIncision()
    {
        // A ramp rising east at 57°, steeper than the soft talus (30°), under the hard one (60°), with no water to speak of: the
        // talus limit alone shapes it, from the outlet at its foot up. The grid's edges do not move, so nothing is stretched when
        // the incision maps its range back, and the angles can be read off directly.
        const int n = 33;
        HeightField ramp = Field(n, 64.0, (i, _) => 100f * i);
        ErosionSettings settings = Rock with { IncisionSteps = 3, IncisionRate = 0f };
        var none = new float[n * n];
        double Angle(float hardness)
        {
            (HeightField shaped, StrataFrame frame) = StreamPower.Incise(ramp, settings, none, (_, _) => hardness);
            Assert.AreEqual(1.0, frame.Scale);
            return Math.Atan((shaped[20, 16] - (double)shaped[10, 16]) / 640.0) * 180.0 / Math.PI;
        }
        Assert.AreEqual(30.0, Angle(0f), 0.01, "the softest rock at its talus");
        double middle = Math.Atan(0.5 * (Math.Tan(30.0 * Math.PI / 180.0) + Math.Tan(60.0 * Math.PI / 180.0))) * 180.0 / Math.PI;
        Assert.AreEqual(middle, Angle(0.5f), 0.01, "between them the talus slope follows the hardness");
        Assert.AreEqual(Math.Atan(100.0 / 64.0) * 180.0 / Math.PI, Angle(1f), 0.01, "the hardest rock keeps the ramp's 57°, under its 60°");
    }

    [TestMethod]
    public void HardRock_StandsSteeperAndErodesLessInTheErosion()
    {
        // A 60° rock wall: the soft rock (40°) collapses, the hard (65°) stands.
        const int n = 33;
        const double cell = 8.0;
        double tan60 = Math.Tan(60.0 * Math.PI / 180.0);
        HeightField wall = Field(n, cell, (i, _) => (float)(Math.Clamp(i - 12, 0, 8) * cell * tan60));
        StrataGrid Uniform(float hardness)
        {
            // One bed of the given hardness everywhere: a two-entry table, no mottle.
            return new StrataGrid([hardness, hardness], 0f, new float[n * n], new float[n * n], StrataFrame.Identity);
        }
        float Loose(StrataGrid strata)
        {
            var state = new ErosionState(wall, strata);
            ErosionConstants k = ErosionConstants.For(Rock, n, cell).WithStrata(state);
            var other = new float[n * n * 2];
            for (int step = 0; step < 500; step++)
            {
                ErosionReference.ThermalOut(k, state.Terrain, state.Thermal, state.Strata, state.Bands);
                ErosionReference.ThermalApply(k, state.Terrain, state.Thermal, other);
                other.CopyTo(state.Terrain, 0);
            }
            return state.Terrain.Where((_, i) => i % 2 == 1).Sum();
        }
        Assert.IsTrue(Loose(Uniform(0f)) > 100f, "the soft wall collapsed into loose material");
        Assert.AreEqual(0f, Loose(Uniform(1f)), "the hard wall stands");

        // The same water on soft and hard rock: the hard loses less.
        HeightField slope = Field(n, cell, (i, _) => 3f * i);
        double Removed(StrataGrid strata)
        {
            var state = new ErosionState(slope, strata);
            ErosionReference.Run(state, Rock with { AverageFrom = 0 }, 200);
            return slope.Heights.ToArray().Sum(h => (double)h) - state.Terrain.Where((_, i) => i % 2 == 0).Sum(r => (double)r);
        }
        double softRemoved = Removed(Uniform(0f)), hardRemoved = Removed(Uniform(1f));
        Assert.IsTrue(softRemoved > 3.0 * hardRemoved && hardRemoved > 0.0, $"soft rock lost {softRemoved:0.00} m over the cells, hard {hardRemoved:0.00} m");
    }

    [TestMethod]
    public void WithoutHardness_TheStrataChangeNothing()
    {
        // HardErodibility 1 and one talus angle: the erosion with the seed's strata is the erosion without them, bit for bit.
        HeightField ground = TerrainGenerator.Generate(Alpine, 3, new WorldGrid(33, 8.0));
        ErosionSettings neutral = ErosionSettings.Mountains with
        {
            Iterations = 60, AverageFrom = 30, HardErodibility = 1f, RockTalusHardDegrees = ErosionSettings.Mountains.RockTalusDegrees,
        };
        StrataGrid strata = StrataGrid.For(RockHardness.For(3, neutral), 33, 8.0, StrataFrame.Identity, (_, _) => 0.0);
        ErosionResult with = ErosionReference.Erode(ground, neutral, strata), without = ErosionReference.Erode(ground, neutral);
        Assert.AreEqual(without.Surface.ContentHash(), with.Surface.ContentHash());
        Assert.IsTrue(with.Hardness.Any(h => h > 0f), "the world still records its rock");
        Assert.IsTrue(without.Hardness.All(h => h == 0f));
    }

    [TestMethod]
    public void TheIncision_StillDrainsEveryBasin_WithUpliftAndHardRock()
    {
        // M1.11a's crater on a dome, now with the uplift map's kind of uplift and the seed's beds.
        const int n = 65;
        HeightField ground = Field(n, 64.0, (i, j) =>
        {
            double r = Math.Sqrt((i - 32) * (i - 32) + (j - 32) * (j - 32)) * 64.0;
            return (float)(1500.0 - 0.3 * r - (r < 600.0 ? 300.0 * (1.0 - r / 600.0) : 0.0));
        });
        var uplift = new float[n * n];
        for (int j = 1; j < n - 1; j++)
        {
            for (int i = 1; i < n - 1; i++)
                uplift[j * n + i] = (float)(4.0 * Math.Max(0.0, 1.0 - Math.Sqrt((i - 32) * (i - 32) + (j - 32) * (j - 32)) / 40.0));
        }
        RockHardness rock = RockHardness.For(1, Rock);
        (HeightField incised, StrataFrame frame) = StreamPower.Incise(ground, Rock, uplift,
            (c, y) => rock.AtSigma(y + rock.Fold(ground.XOf(c % n), ground.ZOf(c / n)), 0f));
        DrainageStats after = DrainageStats.Measure(DrainageMap.Of(incised), channelAreaKm2: 0.1);
        Assert.AreEqual(0.0, after.BasinShare, "the crater drains");
        Assert.AreEqual(ground.MinHeight, incised.MinHeight, 1e-3f);
        Assert.AreEqual(ground.MaxHeight, incised.MaxHeight, 1e-3f);
        Assert.AreEqual(Rock.IncisionSteps, frame.UpliftSteps);
        Assert.AreEqual(ground.MinHeight, frame.Low, 1e-3);
    }
}
