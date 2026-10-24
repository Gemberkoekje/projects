using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The contour-line measure (vista-plan §10 M1.12c verify: "the share of ledge length ... that runs within 10° of the local contour,
/// which level benches drive toward one and folded strata pull down"), on synthetic hills whose beds are known.
/// </summary>
[TestClass]
public sealed class LedgeStatsTests
{
    private const int Size = 257;
    private const double Cell = 8.0;

    // A cone 1000 m high and about a kilometre across: slopes of 40-50° on every side.
    private static HeightField Cone()
    {
        var heights = new float[Size * Size];
        double origin = -0.5 * (Size - 1) * Cell;
        for (int j = 0; j < Size; j++)
        {
            for (int i = 0; i < Size; i++)
            {
                double x = origin + i * Cell, z = origin + j * Cell;
                heights[j * Size + i] = (float)Math.Max(0.0, 1000.0 - 1.1 * Math.Sqrt(x * x + z * z));
            }
        }
        return new HeightField(Size, Cell, heights);
    }

    // Beds 40 m thick, hard and soft by turns, at the stratigraphic height σ = y + dip·x.
    private static float[] Beds(HeightField cone, double dip)
    {
        var hardness = new float[Size * Size];
        double origin = -0.5 * (Size - 1) * Cell;
        for (int j = 0; j < Size; j++)
        {
            for (int i = 0; i < Size; i++)
            {
                double sigma = cone[i, j] + dip * (origin + i * Cell);
                double phase = sigma / 40.0 - Math.Floor(sigma / 40.0);
                hardness[j * Size + i] = (float)(0.5 + 0.4 * Math.Sin(2.0 * Math.PI * phase));
            }
        }
        return hardness;
    }

    [TestMethod]
    public void LevelBeds_RunAlongTheContours_TiltedOnesCrossThem()
    {
        HeightField cone = Cone();
        LedgeStats level = LedgeStats.Bands(cone, Beds(cone, 0.0), _ => true);
        Assert.AreEqual("bands", level.Kind);
        Assert.IsTrue(level.LineCells > 1000, $"{level.LineCells} band cells");
        Assert.IsTrue(level.AlongContour > 0.95, $"level beds: {level.AlongContour:0.0%} along the contour");
        LedgeStats tilted = LedgeStats.Bands(cone, Beds(cone, Math.Tan(40.0 * Math.PI / 180.0)), _ => true);
        Assert.IsTrue(tilted.AlongContour < 0.5 * level.AlongContour, $"beds dipping 40°: {tilted.AlongContour:0.0%} along the contour");
        Assert.AreEqual(1.0, tilted.AngleShares.Sum(), 1e-9);
        LedgeStats steeper = LedgeStats.Bands(cone, Beds(cone, Math.Tan(70.0 * Math.PI / 180.0)), _ => true);
        Assert.IsTrue(steeper.AlongContour < tilted.AlongContour, $"beds dipping 70°: {steeper.AlongContour:0.0%}");
    }

    [TestMethod]
    public void OnlySteepRock_HasLines()
    {
        HeightField cone = Cone();
        float[] beds = Beds(cone, 0.0);
        LedgeStats none = LedgeStats.Bands(cone, beds, _ => false);
        Assert.AreEqual(0, none.RockCells);
        Assert.IsTrue(double.IsNaN(none.AlongContour));
        LedgeStats flatRock = LedgeStats.Bands(cone, new float[Size * Size], _ => true);
        Assert.AreEqual(0, flatRock.LineCells, "rock of one hardness has no beds' lines");
        // Terraces (the cone in level steps): the ledges of high curvature run along the contours too.
        var steps = new float[Size * Size];
        for (int c = 0; c < steps.Length; c++)
            steps[c] = 40f * MathF.Floor(cone.Heights[c] / 40f) + 0.3f * (cone.Heights[c] % 40f);
        LedgeStats terraces = LedgeStats.Ledges(new HeightField(Size, Cell, steps), _ => true);
        Assert.AreEqual("ledges", terraces.Kind);
        Assert.IsTrue(terraces.AlongContour > 0.5, $"terraces: {terraces.AlongContour:0.0%} of the ledges along the contour");
    }
}
