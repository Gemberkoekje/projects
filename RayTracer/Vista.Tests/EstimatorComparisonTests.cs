using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// The pure half of <c>--sampling-compare</c> (vista-plan §10 M1.5): replicate statistics, the per-pixel z-scores that say two
/// estimators agree, the variance ratios that say which is better, and the skyline mask.
/// </summary>
[TestClass]
public sealed class EstimatorComparisonTests
{
    private const int Pixels = 4000;

    /// <summary>Replicates of an image whose pixel p has mean <paramref name="mean"/>(p) and Gaussian noise of standard deviation <paramref name="sigma"/>(p).</summary>
    private static ReplicateStats Replicates(int count, Func<int, double> mean, Func<int, double> sigma, int seed)
    {
        var random = new Random(seed);
        var stats = new ReplicateStats(Pixels);
        for (int k = 0; k < count; k++)
        {
            var xyz = new float[Pixels * 3];
            for (int p = 0; p < Pixels; p++)
            {
                double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
                double normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                xyz[p * 3 + 1] = (float)(mean(p) + sigma(p) * normal);
            }
            stats.Add(xyz);
        }
        return stats;
    }

    private static bool[] AllTerrain() => [.. Enumerable.Repeat(true, Pixels)];

    [TestMethod]
    public void ReplicateStats_GiveTheMeanAndTheVarianceBetweenRenders()
    {
        var stats = new ReplicateStats(2);
        stats.Add([0f, 1f, 0f, 0f, float.NaN, 0f]);
        Assert.AreEqual(0.0, stats.Variance(0), "one render has no spread");
        stats.Add([0f, 3f, 0f, 0f, 4f, 0f]);
        Assert.AreEqual(2, stats.Count);
        Assert.AreEqual(2.0, stats.Mean(0));
        Assert.AreEqual(2.0, stats.Variance(0));
        Assert.AreEqual(1.0, stats.VarianceOfMean(0));
        Assert.AreEqual(2.0, stats.Mean(1), "a non-finite luminance counts as 0");
        Assert.ThrowsExactly<ArgumentException>(() => stats.Add(new float[3]));
    }

    [TestMethod]
    public void SameMean_GivesStandardNormalZ_AndNoGain()
    {
        ReplicateStats a = Replicates(32, p => 1.0 + p % 7, p => 0.1 + 0.01 * (p % 5), 1);
        ReplicateStats b = Replicates(32, p => 1.0 + p % 7, p => 0.1 + 0.01 * (p % 5), 2);
        EstimatorAgreement r = EstimatorComparison.Compare(a, b, AllTerrain());
        Assert.AreEqual(Pixels, r.Pixels);
        Assert.AreEqual(0.0, r.MeanZ, 4.0 / Math.Sqrt(Pixels));
        Assert.AreEqual(1.0, r.RmsZ, 0.06, "a t with 62 degrees of freedom: RMS 1.016");
        Assert.IsTrue(r.FractionBeyond3 < 0.012);
        Assert.AreEqual(0.0, r.MeanLuminanceError, 4.0 * r.MeanLuminanceNoise);
        Assert.AreEqual(1.0, r.VarianceReduction, 0.06);
        Assert.AreEqual(1.0, r.RelativeVarianceReduction, 0.06);
    }

    [TestMethod]
    public void HalfTheNoise_IsFourTimesLessVariance()
    {
        ReplicateStats candidate = Replicates(64, _ => 5.0, _ => 0.1, 3);
        ReplicateStats baseline = Replicates(64, _ => 5.0, _ => 0.2, 4);
        EstimatorAgreement r = EstimatorComparison.Compare(candidate, baseline, AllTerrain());
        Assert.AreEqual(4.0, r.VarianceReduction, 0.2);
        Assert.AreEqual(4.0, r.RelativeVarianceReduction, 0.2);
        Assert.AreEqual(4.0, r.MedianPixelVarianceReduction, 0.4);
    }

    [TestMethod]
    public void ABias_ShowsInTheMeanZAndTheImageMean()
    {
        ReplicateStats candidate = Replicates(32, _ => 1.01, _ => 0.05, 5);
        ReplicateStats baseline = Replicates(32, _ => 1.0, _ => 0.05, 6);
        EstimatorAgreement r = EstimatorComparison.Compare(candidate, baseline, AllTerrain());
        Assert.IsTrue(r.MeanZ > 0.5, "1% against a 1.25% standard error of each pixel's difference");
        Assert.AreEqual(0.01, r.MeanLuminanceError, 0.001);
        Assert.IsTrue(r.MeanLuminanceError > 10.0 * r.MeanLuminanceNoise);
    }

    [TestMethod]
    public void OnlyTerrainPixels_CountTowardTheVariance()
    {
        // Sky pixels are noisy in the baseline only; terrain pixels the same in both.
        ReplicateStats candidate = Replicates(32, _ => 1.0, _ => 0.1, 7);
        ReplicateStats baseline = Replicates(32, _ => 1.0, p => p < Pixels / 2 ? 0.1 : 10.0, 8);
        bool[] terrain = [.. Enumerable.Range(0, Pixels).Select(p => p < Pixels / 2)];
        EstimatorAgreement r = EstimatorComparison.Compare(candidate, baseline, terrain);
        Assert.AreEqual(Pixels / 2, r.TerrainPixels);
        Assert.AreEqual(1.0, r.VarianceReduction, 0.1);
    }

    [TestMethod]
    public void InteriorTerrain_LeavesOutTheSkyAndItsNeighbours()
    {
        const int w = 5, h = 4;
        float inf = float.PositiveInfinity;
        float[] depth =
        [
            inf, inf, inf, inf, inf,
            9f, 9f, inf, 9f, 9f,
            9f, 9f, 9f, 9f, 9f,
            9f, 9f, 9f, 9f, 9f,
        ];
        bool[] mask = EstimatorComparison.InteriorTerrain(depth, w, h);
        bool[] expected =
        [
            false, false, false, false, false,
            false, false, false, false, false,
            true, false, false, false, true,
            true, true, true, true, true,
        ];
        CollectionAssert.AreEqual(expected, mask);
        Assert.ThrowsExactly<ArgumentException>(() => EstimatorComparison.InteriorTerrain(depth, 4, 4));
    }

    [TestMethod]
    public void Compare_NeedsMatchingImagesAndTwoReplicates()
    {
        var one = new ReplicateStats(Pixels);
        one.Add(new float[Pixels * 3]);
        ReplicateStats two = Replicates(2, _ => 1.0, _ => 0.1, 9);
        Assert.ThrowsExactly<ArgumentException>(() => EstimatorComparison.Compare(one, two, AllTerrain()));
        Assert.ThrowsExactly<ArgumentException>(() => EstimatorComparison.Compare(two, two, new bool[3]));
    }
}
