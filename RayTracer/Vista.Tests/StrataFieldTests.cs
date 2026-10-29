using Vista.World;

namespace Vista.Tests;

/// <summary>
/// M1.12c's strata (vista-plan §10 M1.12c verify: "the strata field (a pure function of seed and position; dip and fold within their
/// ranges; the mask's share)"), and that with them off the rock is M1.11d's, bit for bit.
/// </summary>
[TestClass]
public sealed class StrataFieldTests
{
    /// <summary>
    /// M1.11d's rock: none of M1.12c's regional dip, folds, formations or massive regions, none of M1.13's glaciers, and M1.11a's grid
    /// transfer and M1.7's passes (none of M1a.2's).
    /// </summary>
    internal static readonly ErosionSettings Even = ErosionSettings.Mountains with
    {
        StrataRegionalDipDegrees = 0f,
        StrataFoldMetres = 0f,
        StrataFormationMetres = 0f,
        StrataMassiveShare = 0f,
        GlacialSteps = 0,
        GlacierElaMetres = 0f,
        RefineCubic = 0,
        RefineChannelCurves = 0,
        RefineFaceDetail = 0f,
        DischargeSpread = 0,
        ThermalRate = 0.25f,
    };

    [TestMethod]
    public void EvenStrata_AreM1_11dsRock_AndKeepItsCacheKey()
    {
        Assert.IsTrue(Even.EvenStrata);
        Assert.AreEqual(PinnedEvenSettingsHash, Even.ContentHash().ToString("x16"), $"M1.11d's settings keep their key, so its worlds stay in the cache (got {Even.ContentHash():x16})");
        RockHardness rock = RockHardness.For(42, Even);
        Assert.AreEqual("1ac705dc0e62475c", rock.ContentHash().ToString("x16"), "M1.11d's bed table and dip");
        Assert.AreEqual(0.2487916f, rock.At(1234.5, 1500.0, -2345.5), 1e-6f);
        Assert.IsTrue(rock.MassiveTable.All(v => v == 0f));
        for (double x = -16000; x <= 16000; x += 1777)
            Assert.AreEqual(0.0, rock.Massive(x, -0.6 * x), "layered everywhere");
    }

    // M1.12b's ErosionSettings.Mountains.ContentHash(), from main's sidecar (world.erosion.settingsHash).
    private const string PinnedEvenSettingsHash = "8e167baaa8d10c3f";

    [TestMethod]
    public void TheShippedStrata_AreFolded_AndPinned()
    {
        Assert.IsFalse(ErosionSettings.Mountains.EvenStrata);
        RockHardness rock = RockHardness.For(42, ErosionSettings.Mountains);
        Assert.AreEqual(PinnedFoldedHash, rock.ContentHash().ToString("x16"), $"got {rock.ContentHash():x16}");
        Assert.AreEqual(PinnedFoldedAt, rock.At(1234.5, 1500.0, -2345.5), 1e-6f, $"got {rock.At(1234.5, 1500.0, -2345.5):R}");
    }

    private const string PinnedFoldedHash = "a65ec4427377e2df";
    private const float PinnedFoldedAt = 0.60169154f;

    /// <summary>Every part of M1.12c's strata on, at values of the tests' own (the shipped ones are chosen by sheet).</summary>
    private static readonly ErosionSettings Folded = Even with
    {
        StrataRegionalDipDegrees = 30f,
        StrataRegionMetres = 10000f,
        StrataFoldMetres = 400f,
        StrataFoldWavelengthMetres = 5000f,
        StrataFormationMetres = 300f,
        StrataMassiveShare = 0.3f,
    };

    /// <summary>The beds' slope at <c>(x, z)</c>: the gradient of <see cref="RockHardness.Fold"/> by central differences over 10 m.</summary>
    private static (double X, double Z) Dip(RockHardness rock, double x, double z) =>
        ((rock.Fold(x + 5.0, z) - rock.Fold(x - 5.0, z)) / 10.0, (rock.Fold(x, z + 5.0) - rock.Fold(x, z - 5.0)) / 10.0);

    [TestMethod]
    public void TheFoldedField_IsAPureFunctionOfSeedAndPosition()
    {
        Assert.IsFalse(Folded.EvenStrata);
        Assert.AreNotEqual(Even.ContentHash(), Folded.ContentHash(), "folded rock is another world");
        RockHardness a = RockHardness.For(42, Folded), b = RockHardness.For(42, Folded);
        Assert.AreEqual(a.ContentHash(), b.ContentHash());
        Assert.AreNotEqual(a.ContentHash(), RockHardness.For(43, Folded).ContentHash(), "another seed, other beds");
        Assert.AreNotEqual(a.ContentHash(), RockHardness.For(42, Even).ContentHash());
        foreach ((double x, double y, double z) in new[] { (0.0, 1200.0, 0.0), (-9000.5, 300.25, 15000.0), (16000.0, 2400.0, -16000.0) })
        {
            Assert.AreEqual(a.At(x, y, z), b.At(x, y, z));
            Assert.AreEqual(a.Massive(x, z), b.Massive(x, z));
            Assert.IsTrue(a.At(x, y, z) is >= 0f and <= 1f);
        }
    }

    [TestMethod]
    public void TheRegionalDip_IsTheSettingsAtEachRegionsCentre_AndTurnsBetweenThem()
    {
        ErosionSettings regional = Even with { StrataDipDegrees = 0f, StrataRegionalDipDegrees = 30f, StrataRegionMetres = 10000f };
        ErosionSettings none = Even with { StrataDipDegrees = 0f };
        RockHardness rock = RockHardness.For(9, regional), flat = RockHardness.For(9, none);
        double tan = Math.Tan(30.0 * Math.PI / 180.0);
        var strikes = new HashSet<long>();
        for (int m = -1; m <= 1; m++)
        {
            for (int k = -1; k <= 1; k++)
            {
                // The regional part alone: the field less the slow warp both share.
                (double ax, double az) = Dip(rock, k * 10000.0, m * 10000.0);
                (double bx, double bz) = Dip(flat, k * 10000.0, m * 10000.0);
                double slope = Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
                Assert.AreEqual(tan, slope, 1e-3, $"the dip at the region centred on ({k}, {m})");
                strikes.Add((long)Math.Round(Math.Atan2(az - bz, ax - bx) * 1000.0));
            }
        }
        Assert.IsTrue(strikes.Count >= 3, $"{strikes.Count} strikes among nine regions");
        // Between the centres the dip blends: never more than twice the centres' slope (with the warp's ~2° on top).
        for (double z = -16000; z <= 16000; z += 1000)
        {
            for (double x = -16000; x <= 16000; x += 1000)
            {
                (double dx, double dz) = Dip(rock, x, z);
                Assert.IsTrue(Math.Sqrt(dx * dx + dz * dz) <= 2.0 * tan + 0.05, $"the dip at ({x}, {z})");
            }
        }
    }

    [TestMethod]
    public void TheFolds_StayWithinTheirHeight_AndReachIt()
    {
        ErosionSettings folded = Even with { StrataDipDegrees = 0f, StrataFoldMetres = 400f, StrataFoldWavelengthMetres = 5000f };
        RockHardness rock = RockHardness.For(3, folded), flat = RockHardness.For(3, Even with { StrataDipDegrees = 0f });
        double highest = 0.0, steepest = 0.0;
        for (double z = -16000; z <= 16000; z += 250)
        {
            for (double x = -16000; x <= 16000; x += 250)
            {
                double fold = rock.Fold(x, z) - flat.Fold(x, z);
                Assert.IsTrue(Math.Abs(fold) <= 400.0 + 1e-6, $"the fold at ({x}, {z}) is {fold:0.0} m");
                highest = Math.Max(highest, Math.Abs(fold));
                (double ax, double az) = Dip(rock, x, z);
                (double bx, double bz) = Dip(flat, x, z);
                steepest = Math.Max(steepest, Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz)));
            }
        }
        Assert.IsTrue(highest > 0.8 * 400.0, $"the folds reach {highest:0} m");
        // A quintic wave's steepest limb is 7.5 times its height over its wavelength; the wandering axes and the swell bend it a little.
        Assert.IsTrue(steepest > 0.3 * 7.5 * 400.0 / 5000.0 && steepest < 1.6 * 7.5 * 400.0 / 5000.0,
            $"the steepest limb dips {Math.Atan(steepest) * 180.0 / Math.PI:0.0}°");
    }

    [TestMethod]
    public void Formations_StackBedsOfUnevenThickness()
    {
        static List<double> Beds(float[] table)
        {
            var runs = new List<double>();
            int start = 0;
            for (int k = 1; k < table.Length; k++)
            {
                if (table[k] != table[k - 1])
                {
                    if (start > 0)
                        runs.Add((k - start) * RockHardness.TableSpacingMetres);
                    start = k;
                }
            }
            return runs;
        }
        static double Spread(List<double> runs)
        {
            double mean = runs.Average();
            return Math.Sqrt(runs.Sum(r => (r - mean) * (r - mean)) / runs.Count) / mean;
        }
        List<double> even = Beds(RockHardness.For(7, Even).Table);
        List<double> formations = Beds(RockHardness.For(7, Even with { StrataFormationMetres = 300f }).Table);
        Assert.IsTrue(formations.Min() < 0.25 * 60.0, $"thin-bedded formations: the thinnest bed {formations.Min()} m");
        Assert.IsTrue(formations.Max() > 2.5 * 60.0, $"thick-bedded formations: the thickest bed {formations.Max()} m");
        Assert.IsTrue(Spread(formations) > 1.3 * Spread(even), $"bed thickness varies {Spread(formations):0.00} against even beds' {Spread(even):0.00}");
        float[] table = RockHardness.For(7, Even with { StrataFormationMetres = 300f }).Table;
        Assert.IsTrue(table.All(v => v is >= 0f and <= 1f));
        // A formation is one kind of rock throughout: the hardness 100 m above a bed is more like the bed's than even beds' is.
        static double Correlation(float[] t, int lag)
        {
            double mean = t.Average(v => (double)v), variance = t.Sum(v => (v - mean) * (v - mean)) / t.Length;
            double sum = 0.0;
            for (int k = 0; k + lag < t.Length; k++)
                sum += (t[k] - mean) * (t[k + lag] - mean);
            return sum / (t.Length - lag) / variance;
        }
        int hundred = (int)(100.0 / RockHardness.TableSpacingMetres);
        double formed = Correlation(table, hundred), evenly = Correlation(RockHardness.For(7, Even).Table, hundred);
        Assert.IsTrue(formed > evenly + 0.1, $"the hardness 100 m apart correlates {formed:0.00} in formations, {evenly:0.00} in even beds");
    }

    [TestMethod]
    public void TheMassiveMask_CoversItsShare_AndFadesTheBedsIntoMassiveRock()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4 })
        {
            RockHardness rock = RockHardness.For(seed, Folded);
            int massive = 0, total = 0;
            for (double z = -16000; z <= 16000; z += 125)
            {
                for (double x = -16000; x <= 16000; x += 125)
                {
                    double m = rock.Massive(x, z);
                    Assert.IsTrue(m is >= 0.0 and <= 1.0);
                    massive += m > 0.5 ? 1 : 0;
                    total++;
                }
            }
            Assert.AreEqual(0.3, (double)massive / total, 0.02, $"seed {seed}: the massive share");
        }
        RockHardness r = RockHardness.For(5, Folded);
        Assert.IsTrue(r.MassiveTable.All(v => v is >= 0.3f and <= 0.9f), "massive rock is middling to hard");
        for (int k = 1; k < r.MassiveTable.Length; k++)
            Assert.IsTrue(Math.Abs(r.MassiveTable[k] - r.MassiveTable[k - 1]) < 0.01f, "massive rock's hardness changes slowly with height");
        // Fully massive, a place's hardness is massive rock's; fully layered, the bed's; between, a blend.
        foreach (float t in new[] { 4000.25f, 8191.5f, 12000.75f })
        {
            Assert.AreEqual(Math.Clamp(RockHardness.Lookup(r.MassiveTable, t) + 0.05f, 0f, 1f), RockHardness.Of(r.Table, r.MassiveTable, t, 0.05f, 1f), 1e-6f);
            Assert.AreEqual(Math.Clamp(RockHardness.Lookup(r.Table, t) + 0.05f, 0f, 1f), RockHardness.Of(r.Table, r.MassiveTable, t, 0.05f, 0f));
        }
    }

    [TestMethod]
    public void TheGridLayout_CarriesHowMassiveTheRockIs()
    {
        RockHardness rock = RockHardness.For(11, Folded);
        var frame = new StrataFrame(Low: 400.0, Bottom: 650.0, Scale: 1.25, UpliftSteps: 300);
        static double Uplift(double x, double z) => 2.0 + 0.0001 * x;
        const int n = 65;
        const double cell = 512.0;
        StrataGrid grid = StrataGrid.For(rock, n, cell, frame, Uplift);
        double origin = -0.5 * (n - 1) * cell;
        int mismatched = 0, checks = 0;
        for (int j = 0; j < n; j += 4)
        {
            for (int i = 0; i < n; i += 4)
            {
                double x = origin + i * cell, z = origin + j * cell;
                Assert.AreEqual((float)rock.Massive(x, z), grid.Massive[j * n + i]);
                foreach (float y in new[] { 500f, 1333.5f, 2400f })
                {
                    checks++;
                    if (Math.Abs(grid.At(j * n + i, y) - rock.At(x, frame.RockHeight(y, Uplift(x, z)), z)) > 2e-3f)
                        mismatched++;
                }
            }
        }
        Assert.IsTrue(mismatched <= 3, $"{mismatched} of {checks} lookups differ");
        Assert.IsTrue(grid.Massive.Any(m => m > 0.99f) && grid.Massive.Any(m => m < 0.01f), "massive and layered regions both on the grid");
        CollectionAssert.AreEqual(StrataGrid.Interleave(rock.Table, rock.MassiveTable), grid.Bands);
    }

    [TestMethod]
    public void MassiveRock_StandsByItsOwnHardness_InTheErosion()
    {
        // A 60° wall whose beds are the softest rock and whose massive rock the hardest: layered it collapses, massive it stands.
        const int n = 33;
        const double cell = 8.0;
        ErosionSettings settings = Even with { RockTalusDegrees = 40f, RockTalusHardDegrees = 65f };
        double tan60 = Math.Tan(60.0 * Math.PI / 180.0);
        var heights = new float[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
                heights[j * n + i] = (float)(Math.Clamp(i - 12, 0, 8) * cell * tan60);
        }
        var wall = new HeightField(n, cell, heights);
        float Loose(float massive)
        {
            var weights = new float[n * n];
            Array.Fill(weights, massive);
            var strata = new StrataGrid([0f, 0f], [1f, 1f], 0f, new float[n * n], new float[n * n], weights, StrataFrame.Identity);
            var state = new ErosionState(wall, strata);
            ErosionConstants k = ErosionConstants.For(settings, n, cell).WithStrata(state);
            var other = new float[n * n * 2];
            for (int step = 0; step < 500; step++)
            {
                ErosionReference.ThermalOut(k, state.Terrain, state.Thermal, state.Strata, state.Bands);
                ErosionReference.ThermalApply(k, state.Terrain, state.Thermal, other);
                other.CopyTo(state.Terrain, 0);
            }
            return state.Terrain.Where((_, i) => i % 2 == 1).Sum();
        }
        Assert.IsTrue(Loose(0f) > 100f, "layered (soft beds), the wall collapses");
        Assert.AreEqual(0f, Loose(1f), "massive (hard), the wall stands");
    }
}
