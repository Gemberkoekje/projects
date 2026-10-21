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
        Assert.AreSame(ground, StreamPower.Prepare(ground, Alpine, 5, ErosionSettings.Hills));
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

        HeightField incised = StreamPower.Incise(ground, ErosionSettings.Mountains);
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
        var grid = new WorldGrid(129, 16.0);
        ErosionSettings settings = ErosionSettings.Mountains with { IncisionSteps = 60 };
        HeightField ground = TerrainGenerator.Generate(Alpine, 9, grid);
        HeightField a = StreamPower.Prepare(ground, Alpine, 9, settings);
        HeightField b = StreamPower.Prepare(ground, Alpine, 9, settings);
        Assert.AreEqual(a.ContentHash(), b.ContentHash());
        Assert.AreNotEqual(ground.ContentHash(), a.ContentHash());
        Assert.AreEqual(PinnedPreparedHash, a.ContentHash().ToString("x16"));
    }

    private const string PinnedPreparedHash = "96b1fa58db5650a3";

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
