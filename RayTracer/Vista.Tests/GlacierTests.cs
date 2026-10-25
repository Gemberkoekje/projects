using System.Globalization;
using Vista.Recipes;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The glaciers of M1.13 (vista-plan §4.5, §10): the steady ice a climate keeps on the ground (<see cref="GlacierFlow"/>), the glacial
/// carving that widens the incision's valleys (<see cref="StreamPower"/>), today's glaciers laid on a finished world
/// (<see cref="Glaciers"/>), and the cache that keeps them.
/// </summary>
[TestClass]
public sealed class GlacierTests
{
    /// <summary>A cold climate: the line at 1700 m, the Alps' gradient, 2 m of ice a year at most above it, 10 m of melt below.</summary>
    private static readonly GlacierClimate Alpine = new(1700.0, 0.007, 2.0, 10.0);

    /// <summary>
    /// A valley 4 km long on the 64 m grid: its floor falls 0.08 to the south edge (row 0) from 2200 m (1975 m at row 20), its walls
    /// rise <paramref name="wall"/> to either side of the middle column up to <paramref name="cap"/> above the floor, then level out:
    /// a V whose head stands above a line at 2150 m and whose tongue below it, between benches below the line too.
    /// </summary>
    private static HeightField Vee(int n = 65, double cell = 64.0, double wall = 0.3, double cap = 150.0)
    {
        var heights = new float[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
                heights[j * n + i] = (float)(2200.0 - 0.08 * (n - 1 - j) * cell + Math.Min(wall * Math.Abs(i - n / 2) * cell, cap));
        }
        return new HeightField(n, cell, heights);
    }

    /// <summary>A cone 2.4 km high at its middle, sloping 0.35 (19°).</summary>
    private static HeightField Cone(int n, double cell)
    {
        var heights = new float[n * n];
        double middle = 0.5 * (n - 1);
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
                heights[j * n + i] = (float)(2400.0 - 0.35 * cell * Math.Sqrt((i - middle) * (i - middle) + (j - middle) * (j - middle)));
        }
        return new HeightField(n, cell, heights);
    }

    // ── The ice ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FifthRoot_IsArithmetic_AndExact()
    {
        Assert.AreEqual(0.0, GlacierFlow.FifthRoot(0.0));
        Assert.AreEqual(0.0, GlacierFlow.FifthRoot(-3.0));
        Assert.AreEqual(2.0, GlacierFlow.FifthRoot(32.0));
        for (double x = 1e-12; x < 1e14; x *= 7.3)
        {
            double r = GlacierFlow.FifthRoot(x);
            Assert.AreEqual(1.0, r * r * r * r * r / x, 1e-13, x.ToString(CultureInfo.InvariantCulture));
        }
    }

    [TestMethod]
    public void Ice_LiesAboveTheLine_AndFlowsBelowIt_ThenEnds()
    {
        HeightField cone = Cone(65, 64.0);
        IceField ice = GlacierFlow.Solve(cone, Alpine, GlacierFlowSettings.Default, null, 40);
        int n = cone.Size;
        Assert.IsTrue(ice.Thickness[32 * n + 32] > 10f, $"the summit is iced: {ice.Thickness[32 * n + 32]} m");
        // Below the line the ice still flows a way down, and ends before the cone's foot.
        float lowestIce = float.MaxValue;
        for (int c = 0; c < n * n; c++)
        {
            if (ice.Thickness[c] >= 1f)
                lowestIce = Math.Min(lowestIce, cone.Heights[c]);
        }
        Assert.IsTrue(lowestIce < Alpine.ElaMetres, $"the ice flows below its line: lowest {lowestIce} m");
        Assert.IsTrue(lowestIce > 900f, $"and melts before the foot: lowest {lowestIce} m");
        Assert.IsTrue(ice.Thickness.Max() < 600f && ice.Speed.Max() < 2000f, "a mountain glacier's thickness and speed");
    }

    [TestMethod]
    public void Ice_FillsItsValley_AcrossItsWidth()
    {
        HeightField vee = Vee();
        // Row 20's floor stands at 1975 m, its benches at 2125 m, both below the line: there the glacier is a tongue in its valley.
        IceField ice = GlacierFlow.Solve(vee, Alpine with { ElaMetres = 2150.0 }, GlacierFlowSettings.Default, null, 40);
        int n = vee.Size, row = 20;
        int across = 0;
        double topHigh = double.MinValue, topLow = double.MaxValue, bedHigh = double.MinValue, bedLow = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            int c = row * n + i;
            if (ice.Thickness[c] < 10f)
                continue;
            across++;
            // The trough's own cells (the benches beside it carry a thin sheet of the ice that flows down them from above the line).
            if (vee.Heights[c] > vee[n / 2, row] + 100f)
                continue;
            double top = vee.Heights[c] + ice.Thickness[c];
            (topHigh, topLow) = (Math.Max(topHigh, top), Math.Min(topLow, top));
            (bedHigh, bedLow) = (Math.Max(bedHigh, vee.Heights[c]), Math.Min(bedLow, vee.Heights[c]));
        }
        Assert.IsTrue(across >= 5, $"ice 10 m thick across {across} cells (320 m) of the valley");
        Assert.IsTrue(topHigh - topLow < 0.75 * (bedHigh - bedLow), $"over the trough the ice's surface varies {topHigh - topLow:0} m, its bed {bedHigh - bedLow:0} m");
    }

    [TestMethod]
    public void SteepFaces_HoldNoIce()
    {
        HeightField vee = Vee(wall: 1.2, cap: double.MaxValue);
        IceField ice = GlacierFlow.Solve(vee, Alpine with { ElaMetres = 1000.0 }, GlacierFlowSettings.Default, null, 20);
        bool[] steep = GlacierFlow.SteepCells(vee, GlacierFlowSettings.Default.SteepDegrees);
        Assert.IsTrue(steep.Count(s => s) > vee.Size * vee.Size / 2, "50° walls");
        for (int c = 0; c < steep.Length; c++)
        {
            if (steep[c])
                Assert.AreEqual(0f, ice.Thickness[c], "a face steeper than snow lies on holds no ice");
        }
        Assert.IsTrue(ice.Count(10f) > 0, "the floor between them does");
    }

    [TestMethod]
    public void Ice_IsAPureFunctionOfTheGround()
    {
        HeightField cone = Cone(65, 64.0);
        IceField a = GlacierFlow.Solve(cone, Alpine, GlacierFlowSettings.Default, null, 12);
        IceField b = GlacierFlow.Solve(cone, Alpine, GlacierFlowSettings.Default, null, 12);
        CollectionAssert.AreEqual(a.Thickness, b.Thickness);
        CollectionAssert.AreEqual(a.Flux, b.Flux);
    }

    // ── The carving ─────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The power b of a trough's cross-section, <c>z − floor = a·|x|^b</c>, fitted between one and four cells from its floor, both
    /// sides averaged (a V 1, a parabolic U 2; Harbor 1992), and its depth to the benches 16 cells out, at a row.
    /// </summary>
    private static (double Power, double Depth) Trough(HeightField h, int row)
    {
        int f = Enumerable.Range(0, h.Size).OrderBy(i => h[i, row]).First();
        double Rise(int k) => 0.5 * (h[f - k, row] + h[f + k, row]) - h[f, row];
        return (Math.Log(Rise(4) / Rise(1)) / Math.Log(4.0), h[f - 16, row] - h[f, row]);
    }

    [TestMethod]
    public void Carving_DeepensAndRoundsAValleyGlaciersTrough()
    {
        HeightField vee = Vee();
        var uplift = new float[vee.Size * vee.Size];
        ErosionSettings fluvial = ErosionSettings.Mountains with { IncisionSteps = 1, GlacialSteps = 0, GlacierElaMetres = 0f };
        // The line in the world's metres, the head above it and the tongue's valley below it.
        ErosionSettings glacial = fluvial with { GlacialSteps = 60, GlacialElaMetres = 2150f, GlacialElaSwingMetres = 0f, GlacialErosionMetres = 12f };
        HeightField incised = StreamPower.Incise(vee, fluvial, uplift).Incised;
        (HeightField carved, StrataFrame frame) = StreamPower.Incise(vee, glacial, uplift);
        Assert.AreEqual(1, frame.UpliftSteps, "the carving lifts nothing: it works on the finished world");
        foreach (int row in new[] { 15, 20 })
        {
            (double v, double vDepth) = Trough(incised, row);
            (double u, double uDepth) = Trough(carved, row);
            Assert.IsTrue(v < 1.0, $"row {row}: the incision leaves a V (power {v:0.00})");
            Assert.IsTrue(u > v + 0.15, $"row {row}: the glacier rounds it (power {v:0.00} to {u:0.00})");
            Assert.IsTrue(uDepth > vDepth + 20.0, $"row {row}: and deepens it ({vDepth:0} m to {uDepth:0} m)");
        }
        // Determinism: the carving is a pure function of the ground and the settings.
        CollectionAssert.AreEqual(carved.Heights.ToArray(), StreamPower.Incise(vee, glacial, uplift).Incised.Heights.ToArray());
    }

    [TestMethod]
    public void Unglaciated_Settings_KeepM1_12csWorlds()
    {
        ErosionSettings plain = ErosionSettings.Mountains with { GlacialSteps = 0, GlacierElaMetres = 0f };
        Assert.IsTrue(plain.Unglaciated);
        Assert.AreEqual("b7e5666038cc769b", plain.ContentHash().ToString("x16", CultureInfo.InvariantCulture),
            "M1.12c's mountains, so the worlds it eroded are found in the cache");
        Assert.AreNotEqual(plain.ContentHash(), ErosionSettings.Mountains.ContentHash(), "the glaciated mountains are other worlds");
        Assert.AreEqual(0, ErosionSettings.Hills.GlacialSteps, "the hills carry no glaciers");
        Assert.AreEqual(0f, ErosionSettings.Hills.GlacierElaMetres);
        CollectionAssert.AreEqual(Array.Empty<string>(), ErosionSettings.Mountains.Validate().ToArray());
    }

    [TestMethod]
    public void CarvingClimate_SwingsSmoothly_FromItsLowestThroughItsHighest()
    {
        ErosionSettings s = ErosionSettings.Mountains with { GlacialElaMetres = 1500f, GlacialElaSwingMetres = 300f, GlacialCycleSteps = 20 };
        Assert.AreEqual(1200.0, s.CarvingClimate(0).ElaMetres, 1e-9);
        Assert.AreEqual(1800.0, s.CarvingClimate(10).ElaMetres, 1e-9);
        Assert.AreEqual(1500.0, s.CarvingClimate(5).ElaMetres, 1e-9);
        Assert.AreEqual(s.CarvingClimate(3).ElaMetres, s.CarvingClimate(23).ElaMetres);
        for (int k = 0; k < 10; k++)
            Assert.IsTrue(s.CarvingClimate(k + 1).ElaMetres > s.CarvingClimate(k).ElaMetres, "rising through the first half");
    }

    // ── Today's glaciers ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>An eroded result of <paramref name="ground"/> with empty maps (the laying reads only the surface).</summary>
    private static ErosionResult Bare(HeightField ground)
    {
        int cells = ground.Size * ground.Size;
        return new ErosionResult(ground, new float[cells], new float[cells], new float[cells], new float[cells], 0, DrainageMap.Of(ground),
            new float[cells], StrataFrame.Identity);
    }

    [TestMethod]
    public void Glaciers_LieOnTheGround_AndJoinItsSurface()
    {
        HeightField ground = Cone(257, 8.0);
        ErosionResult bare = Bare(ground);
        Assert.AreSame(bare, Glaciers.Lay(bare, ErosionSettings.Mountains with { GlacierElaMetres = 0f }), "no line, no glaciers");
        ErosionResult laid = Glaciers.Lay(bare, ErosionSettings.Mountains with { GlacierElaMetres = 2300f });
        Assert.IsNotNull(laid.Ice);
        int iced = 0;
        for (int c = 0; c < laid.Ice.Length; c++)
        {
            Assert.IsTrue(laid.Ice[c] >= 0f);
            Assert.AreEqual(ground.Heights[c] + laid.Ice[c], laid.Surface.Heights[c], "the surface is the ground plus the ice");
            Assert.AreEqual(ErosionResult.ToHalf(laid.Ice[c]), laid.Ice[c], "in the cache's precision");
            iced += laid.Ice[c] >= 1f ? 1 : 0;
        }
        Assert.IsTrue(iced > 1000, $"{iced} samples under the summit's ice cap");
        Assert.AreEqual(0f, laid.Ice[0], "none at the cone's foot");
        Assert.AreNotEqual(bare.ContentHash(), laid.ContentHash(), "the ice is part of the world");
        Assert.AreSame(bare.Drainage, laid.Drainage, "the water runs under the ice, along its bed");
    }

    // ── The valleys' shape ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A valley along z on the 64 m grid, its floor falling 0.05 to the south, its cross-section <paramref name="across"/>(|x| in m).</summary>
    private static HeightField Valley(Func<double, double> across)
    {
        const int n = 129;
        var heights = new float[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
                heights[j * n + i] = (float)(1500.0 + 0.05 * j * 64.0 + across(Math.Abs(i - n / 2) * 64.0));
        }
        return new HeightField(n, 64.0, heights);
    }

    [TestMethod]
    public void ValleyShape_TellsAVeeFromAU()
    {
        // A V rising 0.4 to either side; a parabola reaching the same 100 m at 500 m out.
        HeightField vee = Valley(x => 0.4 * x), u = Valley(x => 100.0 * (x / 500.0) * (x / 500.0));
        IceField ice = GlacierFlow.Solve(vee, Alpine with { ElaMetres = 1700.0 }, GlacierFlowSettings.Default, null, 30);
        IReadOnlyList<ValleySection> sections = ValleyShape.Measure(u, vee, ice, 4);
        Assert.IsTrue(sections.Count > 0, "the valley's glacier gives sections");
        foreach (ValleySection s in sections)
        {
            // Cut across the ice's flow, which leans a little from straight across the valley, so the cut climbs its floor's slope a
            // little on one side: the shapes within a few hundredths.
            Assert.AreEqual(0.5, s.Incised.HalfDepthRatio, 0.07, "a V: half the width at half the depth");
            Assert.AreEqual(1.0, s.Incised.Power, 0.2);
            Assert.AreEqual(Math.Sqrt(0.5), s.Carved.HalfDepthRatio, 0.07, "a parabola");
            Assert.AreEqual(2.0, s.Carved.Power, 0.4);
            Assert.IsTrue(s.Carved.HalfDepthRatio > s.Incised.HalfDepthRatio + 0.1);
            // The V is 500 m across at 100 m up; a cut at an angle to the valley widens it, and both of its widths alike.
            Assert.IsTrue(s.Incised.WidthMetres is >= 490.0 and < 600.0, $"{s.Incised.WidthMetres:0} m");
        }
    }
}
