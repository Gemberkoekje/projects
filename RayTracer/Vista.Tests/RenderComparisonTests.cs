using Vista.Atmosphere;

namespace Vista.Tests;

/// <summary>The by-distance comparison of two renders behind <c>--atmosphere-compare</c> (vista-plan §10 M1.4).</summary>
[TestClass]
public sealed class RenderComparisonTests
{
    [TestMethod]
    public void Pixels_FallInTheirDistanceBin_SkyLast()
    {
        Assert.AreEqual(0, RenderComparison.BinOf(10f));
        Assert.AreEqual(1, RenderComparison.BinOf(1000f), "bins are [low, high)");
        Assert.AreEqual(4, RenderComparison.BinOf(15_999f));
        Assert.AreEqual(5, RenderComparison.BinOf(40_000f));
        Assert.AreEqual(RenderComparison.DepthBins.Count, RenderComparison.BinOf(float.PositiveInfinity));
    }

    [TestMethod]
    public void Compare_GivesEachBinsMeans_ErrorAndNoise()
    {
        // Three pixels: two terrain pixels at 3 km (Y 2 and 4 against 2 and 2), one sky pixel (Y 5 against 4).
        float[] depth = [3000f, 3500f, float.PositiveInfinity];
        float[] candidate = [0f, 2f, 0f, 0f, 4f, 0f, 0f, 5f, 0f];
        float[] reference = [0f, 2f, 0f, 0f, 2f, 0f, 0f, 4f, 0f];
        float[] candidateVariance = [0.01f, 0.01f, 0.04f];
        float[] referenceVariance = [0.04f, 0.04f, 0f];
        IReadOnlyList<ComparisonBin> bins = RenderComparison.Compare(depth, candidate, candidateVariance, reference, referenceVariance);

        Assert.AreEqual(2, bins.Count, "empty bins are left out");
        ComparisonBin terrain = bins[0];
        Assert.AreEqual(("2–4 km", 2), (terrain.Bin, terrain.Pixels));
        Assert.AreEqual(3.0, terrain.CandidateLuminance, 1e-12);
        Assert.AreEqual(2.0, terrain.ReferenceLuminance, 1e-12);
        Assert.AreEqual(0.5, terrain.Error, 1e-12);
        Assert.AreEqual(Math.Sqrt(0.02) / 2.0, terrain.CandidateStandardError, 1e-7, "the standard error of a mean of two pixels");
        Assert.AreEqual(Math.Sqrt(0.02 / 4.0 + 0.08 / 4.0) / 2.0, terrain.ErrorNoise, 1e-7);
        Assert.AreEqual(("sky", 0.25), (bins[1].Bin, bins[1].Error));
    }

    [TestMethod]
    public void Compare_RefusesMismatchedBuffers()
    {
        Assert.ThrowsExactly<ArgumentException>(() => RenderComparison.Compare([1f], [1f, 2f, 3f], [0f], [1f, 2f], [0f]));
    }
}
