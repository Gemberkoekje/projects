using Vista.Recipes;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The hierarchy the view-dependent mesh refines (vista-plan §10 M1.9): the 4-8 mesh's levels, edges and children; the three nested
/// grids and how they meet; the smooth surface below the world grid; and the saturation that keeps every refinement free of cracks.
/// </summary>
[TestClass]
public sealed class TerrainHierarchyTests
{
    private static readonly Lazy<(HeightField Field, TerrainHierarchy Hierarchy)> Small = new(() =>
    {
        HeightField field = TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.AlpineRange] }, 5, TestWorlds.Small);
        return (field, TerrainHierarchy.Build(field, TerrainProfile.Mountains, 5, new TerrainDetail(5, field.CellSize)));
    });

    [TestMethod]
    public void LevelOf_TellsSquareCentresFromEdgeMidpoints()
    {
        Assert.AreEqual((2, true), TerrainHierarchy.LevelOf(4, 12));    // both odd multiples of 4: a square's centre
        Assert.AreEqual((2, false), TerrainHierarchy.LevelOf(4, 8));    // one: an edge's midpoint
        Assert.AreEqual((3, false), TerrainHierarchy.LevelOf(8, 0));
        Assert.AreEqual((0, true), TerrainHierarchy.LevelOf(3, 5));
    }

    [TestMethod]
    public void SplitEdge_JoinsTwoOlderVertices_AtTheVertexsMidpoint()
    {
        TerrainHierarchy h = Small.Value.Hierarchy;
        var random = new Random(4);
        for (int n = 0; n < 5000; n++)
        {
            int i = random.Next(2, h.RootSize - 1), j = random.Next(2, h.RootSize - 1);
            (int t, bool diagonal) = TerrainHierarchy.LevelOf(i, j);
            int level = h.LevelIndex(t, diagonal);
            (int i0, int j0, int i1, int j1) = TerrainHierarchy.SplitEdge(i, j, t, diagonal);
            Assert.AreEqual((i, j), ((i0 + i1) / 2, (j0 + j1) / 2));
            Assert.AreEqual(h.EdgeLength(level), Math.Sqrt((double)(i1 - i0) * (i1 - i0) + (double)(j1 - j0) * (j1 - j0)) * h.SubUnit, 1e-9);
            foreach ((int ei, int ej) in new[] { (i0, j0), (i1, j1) })
            {
                if (ei is 0 || ej is 0 || ei == h.RootSize || ej == h.RootSize)
                    continue; // a root corner or edge
                (int et, bool ed) = TerrainHierarchy.LevelOf(ei, ej);
                Assert.IsTrue(h.LevelIndex(et, ed) < level, $"({i}, {j})'s end ({ei}, {ej}) is no older");
            }
        }
    }

    [TestMethod]
    public void Children_HaveTheVertexAsAParent()
    {
        // A vertex's parents are the far corners of the two triangles that share its edge: a child lists the vertex among them.
        var random = new Random(5);
        Span<(int I, int J)> children = stackalloc (int, int)[4];
        for (int n = 0; n < 5000; n++)
        {
            int i = random.Next(64, 1 << 20), j = random.Next(64, 1 << 20);
            (int t, bool diagonal) = TerrainHierarchy.LevelOf(i, j);
            int count = TerrainHierarchy.Children(i, j, t, diagonal, children);
            for (int c = 0; c < count; c++)
            {
                (int ci, int cj) = children[c];
                (int ct, bool cd) = TerrainHierarchy.LevelOf(ci, cj);
                (int a0, int b0, int a1, int b1) = TerrainHierarchy.SplitEdge(ci, cj, ct, cd);
                // The parents sit across the edge: the edge's direction turned a quarter, from the child.
                int pi = ci + (b1 - b0) / 2, pj = cj - (a1 - a0) / 2, qi = ci - (b1 - b0) / 2, qj = cj + (a1 - a0) / 2;
                Assert.IsTrue((pi, pj) == (i, j) || (qi, qj) == (i, j), $"({i}, {j}) is not a parent of its child ({ci}, {cj})");
            }
        }
    }

    [TestMethod]
    public void Saturation_GivesEveryParentAtLeastItsChildrensError_AndASphereAroundTheirs()
    {
        (_, TerrainHierarchy h) = Small.Value;
        Span<(int I, int J)> children = stackalloc (int, int)[4];
        int checkedPairs = 0;
        // Every vertex of store 0 (g0's square and its margin), every level, and its children stored or not.
        int low = h.G0Low - TerrainHierarchy.Margin0Cells * TerrainHierarchy.Sub, high = h.G0High + TerrainHierarchy.Margin0Cells * TerrainHierarchy.Sub;
        for (int j = low; j <= high; j += TerrainHierarchy.Sub)
        {
            for (int i = low; i <= high; i += TerrainHierarchy.Sub)
            {
                Assert.IsTrue(h.TryStored(i, j, out float height, out float error, out float radius), $"({i}, {j}) is stored");
                (int t, bool diagonal) = TerrainHierarchy.LevelOf(i, j);
                int level = h.LevelIndex(t, diagonal);
                int count = TerrainHierarchy.Children(i, j, t, diagonal, children);
                for (int c = 0; c < count; c++)
                {
                    (int ci, int cj) = children[c];
                    (float ch, float ce, float cr) = h.TryStored(ci, cj, out float sh, out float se, out float sr)
                        ? (sh, se, sr)
                        : (h.BaseHeight(ci, cj), h.AnalyticError(level + 1), h.AnalyticRadius(level + 1));
                    double dx = (ci - i) * h.SubUnit, dz = (cj - j) * h.SubUnit, dy = ch - height;
                    Assert.IsTrue(error >= ce, $"({i}, {j}) error {error} < child's {ce}");
                    Assert.IsTrue(radius >= Math.Sqrt(dx * dx + dy * dy + dz * dz) + cr - 1e-3, $"({i}, {j}) radius {radius} misses its child's sphere");
                    checkedPairs++;
                }
            }
        }
        Assert.IsTrue(checkedPairs > 10_000);
    }

    [TestMethod]
    public void TheWorldGrid_IsItself_InsideItsBlend()
    {
        (HeightField field, TerrainHierarchy h) = Small.Value;
        int band = (field.Size - 1) / 8;
        for (int j = band; j < field.Size - band; j += 3)
        {
            for (int i = band; i < field.Size - band; i += 3)
                Assert.AreEqual(field[i, j], h.BaseHeight(h.G0Low + i * TerrainHierarchy.Sub, h.G0Low + j * TerrainHierarchy.Sub), $"({i}, {j})");
        }
    }

    [TestMethod]
    public void AtItsEdge_TheWorldGridIsTheFarField()
    {
        // On g0's edge the blend has given way to g1: there, every fourth sample is g1's own, the base terrain's coarse octaves.
        (HeightField field, TerrainHierarchy h) = Small.Value;
        ulong seed = SeedDerivation.Derive(5, SeedDerivation.TerrainBase);
        int octaves = TerrainProfile.Mountains.OctavesFor(TerrainHierarchy.Ratio1 * field.CellSize);
        for (int k = 0; k < field.Size; k += TerrainHierarchy.Ratio1)
        {
            int i = h.G0Low + k * TerrainHierarchy.Sub, j = h.G0Low;
            float expected = (float)TerrainProfile.Mountains.Height(seed, h.WorldOf(i), h.WorldOf(j), octaves);
            Assert.AreEqual(expected, h.BaseHeight(i, j), 1e-3f, $"edge sample {k}");
        }
    }

    [TestMethod]
    public void BelowTheGrid_TheSurfaceIsSmooth_AndPassesThroughTheSamples()
    {
        // On a tilted plane Catmull–Rom is the plane itself; on the real field it meets every sample.
        const int size = 65;
        var heights = new float[size * size];
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
                heights[j * size + i] = (float)(0.3 * i * 8.0 - 0.2 * j * 8.0 + 900.0);
        }
        var plane = new HeightField(size, 8.0, heights);
        TerrainHierarchy h = TerrainHierarchy.Build(plane, TerrainProfile.Hills, 1, TerrainDetail.None);
        var random = new Random(6);
        int sub = TerrainHierarchy.Sub;
        for (int n = 0; n < 2000; n++)
        {
            // Well inside, away from the blend into the far field.
            int i = h.G0Low + random.Next(16 * sub, 48 * sub), j = h.G0Low + random.Next(16 * sub, 48 * sub);
            double expected = 0.3 * (i - h.G0Low) * h.SubUnit - 0.2 * (j - h.G0Low) * h.SubUnit + 900.0;
            Assert.AreEqual(expected, h.SubGridHeight(i, j, float.NaN), 2e-4, $"({i}, {j})");
        }

        (HeightField field, TerrainHierarchy real) = Small.Value;
        Assert.AreEqual(field[20, 30], real.SubGridHeight(real.G0Low + 20 * sub, real.G0Low + 30 * sub, float.NaN), 1e-3f);
        Assert.AreEqual(-7f, real.SubGridHeight(3, 5, -7f), "far from store 0: the edge's midpoint");
    }

    [TestMethod]
    public void Build_IsDeterministic()
    {
        (HeightField field, TerrainHierarchy a) = Small.Value;
        TerrainHierarchy b = TerrainHierarchy.Build(field, TerrainProfile.Mountains, 5, new TerrainDetail(5, field.CellSize));
        var random = new Random(7);
        for (int n = 0; n < 3000; n++)
        {
            int i = random.Next(0, a.RootSize + 1), j = random.Next(0, a.RootSize + 1);
            Assert.AreEqual(a.BaseHeight(i, j), b.BaseHeight(i, j));
            Assert.AreEqual(a.TryStored(i, j, out _, out float ea, out float ra), b.TryStored(i, j, out _, out float eb, out float rb));
            Assert.AreEqual((ea, ra), (eb, rb));
        }
    }

    [TestMethod]
    public void Build_NeedsTwoToTheNPlusOneSamples() =>
        Assert.ThrowsExactly<ArgumentException>(() => TerrainHierarchy.Build(new HeightField(60, 8.0, new float[3600]), TerrainProfile.Hills, 1, TerrainDetail.None));

    [TestMethod]
    public void ThePrefilteredTerrain_KeepsItsOctaves_AndItsMean()
    {
        TerrainProfile p = TerrainProfile.Mountains;
        Assert.AreEqual(9, p.OctavesFor(8.0));
        Assert.AreEqual(7, p.OctavesFor(32.0), "down to 94 m");
        Assert.AreEqual(4, p.OctavesFor(256.0), "down to 750 m");
        Assert.AreEqual(1, p.OctavesFor(1e6));
        Assert.AreEqual(p.Height(3, 1234.5, -987.25), p.Height(3, 1234.5, -987.25, p.Octaves), "all octaves: bit for bit");

        var random = new Random(8);
        double full = 0.0, coarse = 0.0;
        const int count = 20_000;
        for (int n = 0; n < count; n++)
        {
            double x = random.NextDouble() * 200_000.0, z = random.NextDouble() * 200_000.0;
            full += p.Height(3, x, z);
            coarse += p.Height(3, x, z, 4);
        }
        Assert.AreEqual(full / count, coarse / count, 15.0, "the octaves left out are replaced by their mean");
    }
}
