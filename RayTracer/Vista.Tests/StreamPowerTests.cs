using Vista.Recipes;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The stream-power incision that drains the base terrain before the pipe-model erosion (vista-plan §10 M1.11a): it opens closed
/// basins, keeps the ground in the base terrain's range, is a pure function of its inputs, and its grid transfer neither moves the
/// coarse samples nor leaves saddles across the channels.
/// </summary>
[TestClass]
public sealed class StreamPowerTests
{
    private static readonly Recipe Alpine = new() { SetPieces = [SetPiece.AlpineRange] };

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
    public void WithoutIncision_TheGroundIsUntouched()
    {
        HeightField ground = TerrainGenerator.Generate(Alpine, 5, TestWorlds.Small);
        (HeightField prepared, StrataFrame frame, HeightField? uplift) = StreamPower.Prepare(ground, Alpine, 5, ErosionSettings.Hills);
        Assert.AreSame(ground, prepared);
        Assert.AreEqual(StrataFrame.Identity, frame, "the rock stays where it was");
        Assert.IsNull(uplift);
        Assert.AreEqual(0, ErosionSettings.Hills.IncisionSteps, "the hills are not incised until their own milestone");
        Assert.IsTrue(ErosionSettings.Mountains.IncisionSteps > 0);
    }

    [TestMethod]
    public void TheIncisionGrid_MustNestInTheWorldGrid()
    {
        Assert.AreEqual(8, StreamPower.CoarseFactor(4097, 8.0, 64.0));
        Assert.AreEqual(4, StreamPower.CoarseFactor(4097, 8.0, 32.0));
        Assert.AreEqual(1, StreamPower.CoarseFactor(65, 512.0, 64.0), "a world grid already as coarse is incised as it is");
        Assert.ThrowsExactly<ArgumentException>(() => StreamPower.CoarseFactor(4097, 8.0, 48.0));
        Assert.ThrowsExactly<ArgumentException>(() => StreamPower.CoarseFactor(4097, 8.0, 60.0));
    }

    [TestMethod]
    public void Incision_DrainsAClosedBasin_AndKeepsTheRangeOfHeights()
    {
        // A dome falling to every edge with a crater 300 m deep in it: the crater holds a lake until its rim is cut.
        const int n = 65;
        HeightField ground = Field(n, 64.0, (i, j) =>
        {
            double r = Math.Sqrt((i - 32) * (i - 32) + (j - 32) * (j - 32)) * 64.0;
            return (float)(1500.0 - 0.3 * r - (r < 600.0 ? 300.0 * (1.0 - r / 600.0) : 0.0));
        });
        DrainageStats before = DrainageStats.Measure(DrainageMap.Of(ground), channelAreaKm2: 0.1);
        Assert.IsTrue(before.BasinShare > 0.02, $"the crater is a closed basin ({before.BasinShare:0.0%} of the grid)");

        // The rivers alone (M1.13's glacial carving after them lowers what its ice covers, the summit's cap here).
        ErosionSettings rivers = ErosionSettings.Mountains with { GlacialSteps = 0 };
        HeightField incised = StreamPower.Incise(ground, rivers, UpliftMap.On(Alpine, 1, rivers, ground)).Incised;
        DrainageStats after = DrainageStats.Measure(DrainageMap.Of(incised), channelAreaKm2: 0.1);
        Assert.AreEqual(0.0, after.BasinShare, "the rim is cut and the crater drains");
        Assert.IsTrue(after.LakeShare < before.LakeShare / 4, $"lakes {before.LakeShare:0.0%} → {after.LakeShare:0.0%}");
        Assert.AreEqual(ground.MinHeight, incised.MinHeight, 1e-3f);
        Assert.AreEqual(ground.MaxHeight, incised.MaxHeight, 1e-3f);
    }

    [TestMethod]
    public void Prepare_IsAPureFunction_AndPinned()
    {
        // The world grid 4× finer than the incision grid, so the transfer (upsampling, carving, the detail) runs too.
        // M1.11d's even beds keep M1.11d's pin; M1.12c's folded strata have their own.
        var grid = new WorldGrid(129, 16.0);
        HeightField ground = TerrainGenerator.Generate(Alpine, 9, grid);
        foreach ((ErosionSettings rock, string pinned) in new[] { (StrataFieldTests.Even, PinnedPreparedHash), (ErosionSettings.Mountains, PinnedFoldedPreparedHash) })
        {
            ErosionSettings settings = rock with { IncisionSteps = 60 };
            HeightField a = StreamPower.Prepare(ground, Alpine, 9, settings).Ground;
            HeightField b = StreamPower.Prepare(ground, Alpine, 9, settings).Ground;
            Assert.AreEqual(a.ContentHash(), b.ContentHash());
            Assert.AreNotEqual(ground.ContentHash(), a.ContentHash());
            Assert.AreEqual(pinned, a.ContentHash().ToString("x16"), $"got {a.ContentHash():x16}");
        }
    }

    private const string PinnedPreparedHash = "6607fcdede7e57c0";
    private const string PinnedFoldedPreparedHash = "ec27e4a974b96d28";

    [TestMethod]
    public void Upsampling_KeepsTheCoarseSamples_AndInterpolatesBetweenThem()
    {
        HeightField coarse = Field(5, 32.0, (i, j) => (float)(StableHash.Lattice(3, i, j) % 100));
        float[] fine = StreamPower.Upsample(coarse, 4);
        const int n = 17;
        Assert.AreEqual(n * n, fine.Length);
        for (int j = 0; j < 5; j++)
        {
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(coarse[i, j], fine[j * 4 * n + i * 4], $"coarse sample ({i}, {j})");
        }
        float centre = (coarse[1, 1] + coarse[2, 1] + coarse[1, 2] + coarse[2, 2]) / 4f;
        Assert.AreEqual(centre, fine[6 * n + 6], 1e-4f, "the middle of a cell is its corners' mean");
    }

    [TestMethod]
    public void SmoothUpsampling_KeepsTheCoarseSamples_AndAPlane_WithoutCreasesOrNewHumps()
    {
        // Through every coarse sample, like the bilinear one.
        HeightField coarse = Field(9, 64.0, (i, j) => (float)(StableHash.Lattice(5, i, j) % 200));
        const int factor = 8, n = 65;
        float[] fine = StreamPower.UpsampleSmooth(coarse, factor);
        Assert.AreEqual(n * n, fine.Length);
        for (int j = 0; j < 9; j++)
        {
            for (int i = 0; i < 9; i++)
                Assert.AreEqual(coarse[i, j], fine[j * factor * n + i * factor], 1e-3f, $"coarse sample ({i}, {j})");
        }
        // Between two samples it stays between them along a coarse row (monotone: no hump or hollow the samples do not have).
        for (int j = 0; j < 9; j++)
        {
            for (int ic = 0; ic < 8; ic++)
            {
                float a = coarse[ic, j], b = coarse[ic + 1, j];
                for (int t = 1; t < factor; t++)
                {
                    float v = fine[j * factor * n + ic * factor + t];
                    Assert.IsTrue(v >= Math.Min(a, b) - 1e-3f && v <= Math.Max(a, b) + 1e-3f, $"row {j}, cell {ic}: {v} outside [{a}, {b}]");
                }
            }
        }
        // A plane comes back as itself.
        HeightField plane = Field(9, 64.0, (i, j) => 100f + 7f * i - 3f * j);
        float[] flat = StreamPower.UpsampleSmooth(plane, factor);
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
                Assert.AreEqual(100.0 + 7.0 * i / factor - 3.0 * j / factor, flat[j * n + i], 1e-3, $"({i}, {j})");
        }
        // No crease along a coarse line: across a sample where a ridge bends, the bilinear slope jumps, the smooth one does not.
        HeightField bend = Field(9, 64.0, (i, j) => 40f * i + (i > 4 ? 60f * (i - 4) : 0f));
        float[] linear = StreamPower.Upsample(bend, factor), smooth = StreamPower.UpsampleSmooth(bend, factor);
        double Kink(float[] f) => Math.Abs(f[4 * factor + 1] - 2.0 * f[4 * factor] + f[4 * factor - 1]);
        Assert.IsTrue(Kink(smooth) < Kink(linear) / 4.0, $"the slope's jump at the sample: {Kink(smooth):0.00} m smooth, {Kink(linear):0.00} m bilinear");
    }

    [TestMethod]
    public void CarvingTheChannels_TakesTheSaddlesOutOfADiagonalChannel_AlongACurve()
    {
        // The diagonal V-shaped valley again, smoothly upsampled: its floor has a saddle in every coarse cell all the same (a tensor
        // product cannot follow a diagonal). Carved along its curve the floor falls steadily, the coarse samples keep their heights,
        // and nothing farther than a coarse cell from the channel moves. Only the floor's cells drain enough to be channels (the side
        // cells drain one to three cells each; the floor from (6, 6) down six and more).
        const int nc = 9, factor = 4;
        HeightField coarse = Field(nc, 64.0, (i, j) => 40f * Math.Abs(i - j) + 5f * (i + j));
        float[] fine = StreamPower.UpsampleSmooth(coarse, factor);
        int n = (nc - 1) * factor + 1;
        float Floor(float[] f, int k) => f[k * n + k];
        Assert.IsTrue(Floor(fine, 6) > Floor(fine, 8), "upsampled, the floor rises across a coarse cell (a saddle)");
        float far = fine[2 * n + 30];
        float[] metres = StreamPower.CarveChannels(fine, coarse, factor, channelAreaKm2: 0.02, curves: 2, wallSlope: 1.0);
        for (int k = 1; k <= (nc - 3) * factor; k++)
            Assert.IsTrue(Floor(fine, k) > Floor(fine, k - 1), $"the carved floor falls steadily at {k}");
        Assert.AreEqual(coarse[2, 2], Floor(fine, 8), 1e-3f, "the coarse samples on a straight run keep their heights");
        Assert.AreEqual(far, fine[2 * n + 30], "far from the channel nothing moves");
        Assert.AreEqual(0f, metres[8 * n + 8], 1e-4f, "on the channel");
        Assert.AreEqual(float.MaxValue, metres[2 * n + 30], "beyond a coarse cell");
    }

    [TestMethod]
    public void CuttingCorners_TurnsAZigzagIntoACurve_KeepingItsEnds()
    {
        // A channel running east-north-east is a zigzag of east and north-east links; three cuts make it a curve that keeps its ends,
        // stays between the zigzag's two rows of corners, passes none of its inner corners, and falls wherever the links fell.
        var zigzag = new List<(double X, double Z, double H)>();
        for (int k = 0; k <= 8; k++)
            zigzag.Add((k, (k + 1) / 2, 100.0 - 5.0 * k));
        List<(double X, double Z, double H)> curve = zigzag;
        for (int cut = 0; cut < 3; cut++)
            curve = StreamPower.CutCorners(curve);
        Assert.AreEqual(zigzag[0], curve[0]);
        Assert.AreEqual(zigzag[^1], curve[^1]);
        for (int k = 1; k < curve.Count; k++)
            Assert.IsTrue(curve[k].H < curve[k - 1].H, $"falls at point {k}");
        foreach ((double x, double z, _) in curve)
            Assert.IsTrue(z >= Math.Floor((x + 1) / 2) - 0.5 - 1e-9 && z <= Math.Ceiling((x + 1) / 2) + 1e-9, $"({x:0.00}, {z:0.00}) strays from the zigzag");
        for (int k = 1; k < 8; k++)
        {
            double nearest = curve.Min(p => Math.Sqrt((p.X - zigzag[k].X) * (p.X - zigzag[k].X) + (p.Z - zigzag[k].Z) * (p.Z - zigzag[k].Z)));
            Assert.IsTrue(nearest > 0.05, $"the curve cuts corner {k} (passes {nearest:0.000} cells from it)");
        }
    }

    [TestMethod]
    public void CarvingTheLinks_TakesTheSaddlesOutOfADiagonalChannel()
    {
        // A V-shaped valley running diagonally (its floor on i = j) and falling toward (0, 0). Bilinear upsampling puts a saddle
        // in the middle of every coarse cell on the floor; carving brings the floor back to a steady fall (up to the last coarse
        // sample before the edge, which is an outlet and drains nowhere).
        const int nc = 9, factor = 4;
        HeightField coarse = Field(nc, 64.0, (i, j) => 40f * Math.Abs(i - j) + 5f * (i + j));
        float[] fine = StreamPower.Upsample(coarse, factor);
        int n = (nc - 1) * factor + 1;
        float Floor(float[] f, int k) => f[k * n + k];
        Assert.IsTrue(Floor(fine, 6) > Floor(fine, 8), "upsampled, the floor rises across a coarse cell (a saddle)");
        float offFloor = fine[5 * n + 10];
        StreamPower.CarveLinks(fine, coarse, factor, channelAreaKm2: 0.004);
        for (int k = 1; k <= (nc - 2) * factor; k++)
            Assert.IsTrue(Floor(fine, k) > Floor(fine, k - 1), $"the carved floor falls steadily at {k}");
        Assert.AreEqual(offFloor, fine[5 * n + 10], "off the links nothing moves");
        Assert.AreEqual(coarse[2, 2], Floor(fine, 8), "the coarse samples keep their heights");
    }
}
