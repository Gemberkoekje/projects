using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The drainage (vista-plan §10 M1.11a verify: "flow accumulation on synthetic surfaces (a tilted plane, a V-shaped valley, a pit)
/// and its determinism"), and the measures that decide the slice on a network built by hand.
/// </summary>
[TestClass]
public sealed class DrainageTests
{
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

    // The neighbour index of a step (di, dj) in DrainageMap's order.
    private static byte Step(int di, int dj)
    {
        for (byte q = 0; q < 8; q++)
        {
            if (DrainageMap.Di[q] == di && DrainageMap.Dj[q] == dj)
                return q;
        }
        throw new ArgumentException("Not a neighbour.");
    }

    [TestMethod]
    public void ATiltedPlane_DrainsStraightDownhill_AndGathersAlongEachRow()
    {
        const int n = 17;
        DrainageMap map = DrainageMap.Of(Field(n, 8.0, (i, _) => 3f * i));
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int c = j * n + i;
                bool edge = i == 0 || j == 0 || i == n - 1 || j == n - 1;
                Assert.AreEqual(edge ? DrainageMap.Outlet : Step(-1, 0), map.Direction[c], $"cell ({i}, {j})");
                Assert.AreEqual(0f, map.FillDepth[c]);
                // Interior cells gather the cells east of them in their row, up to the last interior column.
                if (!edge)
                    Assert.AreEqual(n - 1 - i, map.Upstream[c], $"cell ({i}, {j})");
            }
        }
        Assert.AreEqual(n - 1, map.Upstream[(n / 2) * n], "the west edge collects the whole row");
        Assert.AreEqual(8.0, map.StepLength((n / 2) * n + 3));
        Assert.AreEqual(64.0 * (n - 1), map.Area((n / 2) * n), 1e-9);
    }

    [TestMethod]
    public void AVShapedValley_GathersItsFlanksIntoTheFloor_AndGrowsDownTheValley()
    {
        // The floor runs along z at i = 12 and falls to the south edge; the flanks fall ten times faster toward it.
        const int n = 25, mid = 12;
        DrainageMap map = DrainageMap.Of(Field(n, 8.0, (i, j) => 5f * Math.Abs(i - mid) + 0.5f * j));
        for (int j = 1; j < n - 1; j++)
        {
            Assert.AreEqual(Step(0, -1), map.Direction[j * n + mid], $"the floor at row {j} runs south");
            Assert.AreEqual(Step(1, 0), map.Direction[j * n + mid - 3], "the west flank drains east, into the floor");
            Assert.AreEqual(Step(-1, 0), map.Direction[j * n + mid + 3], "the east flank drains west, into the floor");
            if (j > 1)
                Assert.IsTrue(map.Upstream[(j - 1) * n + mid] > map.Upstream[j * n + mid], $"the floor gains at row {j - 1}");
        }
        int largest = Enumerable.Range(0, n * n).Where(c => map.Direction[c] != DrainageMap.Outlet).MaxBy(c => map.Upstream[c]);
        Assert.AreEqual((1, mid), (largest / n, largest % n), "the most land drains through the floor's last inner cell");
        Assert.AreEqual((n - 2) * (n - 2), map.Upstream[largest], "every inner cell drains through it");
    }

    [TestMethod]
    public void APit_FillsToItsRim_AndDrainsOverIt()
    {
        // A plane falling to the west with a 3×3 hole 5 m deep: the hole fills to its lowest rim (its west side) and its water
        // carries on downhill.
        const int n = 17;
        HeightField field = Field(n, 8.0, (i, j) => 3f * i - (Math.Abs(i - 8) <= 1 && Math.Abs(j - 8) <= 1 ? 30f : 0f));
        DrainageMap map = DrainageMap.Of(field);
        float rim = field[6, 8];
        for (int j = 7; j <= 9; j++)
        {
            for (int i = 7; i <= 9; i++)
                Assert.AreEqual(rim - field[i, j], map.FillDepth[j * n + i], 1e-3f, $"cell ({i}, {j}) fills to the rim");
        }
        Assert.AreEqual(0f, map.FillDepth[8 * n + 12], "the plane around it does not fill");
        int pit = 8 * n + 7;
        Assert.AreEqual(Step(-1, 0), map.Direction[pit], "the pit's west cell drains over the rim");
        Assert.IsTrue(Enumerable.Range(7, 3).Sum(j => map.Upstream[j * n + 6]) >= 9 + 3, "the rim cells carry the hole's water");
        Assert.AreEqual(n * n, map.Order.Distinct().Count(), "every cell is ordered once");
    }

    [TestMethod]
    public void TheDrainage_IsDeterministic_AndRebuildsFromItsDirections()
    {
        const int n = 33;
        HeightField noise = Field(n, 8.0, (i, j) => (float)(StableHash.Lattice(11, i, j) % 500));
        DrainageMap a = DrainageMap.Of(noise);
        DrainageMap b = DrainageMap.Of(noise);
        CollectionAssert.AreEqual(a.Direction, b.Direction);
        CollectionAssert.AreEqual(a.FillDepth, b.FillDepth);
        Assert.AreEqual(a.ContentHash(), b.ContentHash());
        var rebuilt = new DrainageMap(n, 8.0, (byte[])a.Direction.Clone(), (float[])a.FillDepth.Clone());
        CollectionAssert.AreEqual(a.Upstream, rebuilt.Upstream);
        Assert.AreEqual(n * n, a.Order.Length);
        Assert.IsTrue(a.FillDepth.All(d => d >= 0f) && a.FillDepth.Any(d => d > 0f), "noise has hollows, and fills them");
        Assert.IsTrue(a.FillDepth.All(d => d == ErosionResult.ToHalf(d)), "the fill depths are halves");
        int[] position = new int[n * n];
        for (int k = 0; k < a.Order.Length; k++)
            position[a.Order[k]] = k;
        for (int c = 0; c < n * n; c++)
        {
            int r = a.Receiver(c);
            Assert.IsTrue(r < 0 || position[r] > position[c], $"cell {c} comes before the cell it drains into");
        }
    }

    [TestMethod]
    public void ALoop_OrADirectionOffTheGrid_IsInvalidData()
    {
        const int n = 5;
        var directions = Enumerable.Repeat(DrainageMap.Outlet, n * n).ToArray();
        directions[2 * n + 2] = Step(1, 0);
        directions[2 * n + 3] = Step(-1, 0);
        Assert.ThrowsExactly<InvalidDataException>(() => new DrainageMap(n, 8.0, directions, new float[n * n]));
        directions[2 * n + 3] = DrainageMap.Outlet;
        directions[2 * n + 2] = 9;
        Assert.ThrowsExactly<InvalidDataException>(() => new DrainageMap(n, 8.0, directions, new float[n * n]));
    }

    /// <summary>
    /// A hand-built network on 1 km cells: two branches meet at (3, 3) and run south to the edge at (3, 0), each branch two cells
    /// long; every other cell is an outlet of its own. With the channel threshold at 2 km² the branch heads are hillslope.
    /// </summary>
    private static DrainageMap Confluence(float lakeAt32 = 0f)
    {
        const int n = 7;
        var directions = Enumerable.Repeat(DrainageMap.Outlet, n * n).ToArray();
        void Link(int i, int j, int di, int dj) => directions[j * n + i] = Step(di, dj);
        Link(1, 5, 1, -1);
        Link(2, 4, 1, -1);
        Link(5, 5, -1, -1);
        Link(4, 4, -1, -1);
        Link(3, 3, 0, -1);
        Link(3, 2, 0, -1);
        Link(3, 1, 0, -1);
        var fill = new float[n * n];
        fill[2 * n + 3] = lakeAt32;
        return new DrainageMap(n, 1000.0, directions, fill);
    }

    [TestMethod]
    public void Measures_CountStrahlerOrders_LengthsAndShares()
    {
        DrainageMap map = Confluence();
        CollectionAssert.AreEqual(new[] { 1, 2, 1, 2, 5, 6, 7, 8 },
            new[] { (1, 5), (2, 4), (5, 5), (4, 4), (3, 3), (3, 2), (3, 1), (3, 0) }.Select(p => map.Upstream[p.Item2 * 7 + p.Item1]).ToArray());
        DrainageStats s = DrainageStats.Measure(map, channelAreaKm2: 2.0);
        CollectionAssert.AreEqual(new[] { 2, 1 }, s.Streams.ToArray(), "two order-1 streams make one of order 2");
        Assert.AreEqual(2, s.MaxOrder);
        Assert.AreEqual(2.0, s.BifurcationRatio, 1e-12);
        double longest = (Math.Sqrt(2.0) + 3.0) * 1000.0 / 1000.0;
        Assert.AreEqual(longest, s.LongestRouteKm, 1e-9);
        Assert.AreEqual(longest, s.LongestChannelKm, 1e-9);
        Assert.AreEqual(longest, s.LongestOpenChannelKm, 1e-9);
        Assert.AreEqual(6.0 / 49, s.ChannelShare, 1e-12);
        Assert.AreEqual(8.0 / 49, s.DrainsToChannels, 1e-12, "the branch heads drain into channels; the lone outlets do not");
        Assert.AreEqual(8.0 / 49, s.LargestBasinShare, 1e-12);
        Assert.AreEqual(0.0, s.LakeShare);
        Assert.AreEqual(0.0, s.BasinShare);
    }

    [TestMethod]
    public void Measures_BreakChannelsAtLakes_AndAtClosedBasins()
    {
        // A 5 m pool at (3, 2) is a lake but not a closed basin; a 50 m one is both.
        DrainageStats pool = DrainageStats.Measure(Confluence(5f), channelAreaKm2: 2.0);
        Assert.AreEqual(Math.Sqrt(2.0), pool.LongestChannelKm, 1e-9, "the channel stops at the pool");
        Assert.AreEqual(Math.Sqrt(2.0) + 3.0, pool.LongestOpenChannelKm, 1e-9, "a pool does not close the valley");
        Assert.AreEqual(1.0 / 49, pool.LakeShare, 1e-12);
        Assert.AreEqual(0.0, pool.BasinShare);
        Assert.AreEqual(7.0 / 49, pool.DrainsToChannels, 1e-12, "the pool's own cell does not count");

        DrainageStats basin = DrainageStats.Measure(Confluence(50f), channelAreaKm2: 2.0);
        Assert.AreEqual(Math.Sqrt(2.0), basin.LongestOpenChannelKm, 1e-9);
        Assert.AreEqual(1.0 / 49, basin.BasinShare, 1e-12);
        Assert.AreEqual(Math.Sqrt(2.0) + 3.0, basin.LongestRouteKm, 1e-9, "the route runs on through it");
    }
}
