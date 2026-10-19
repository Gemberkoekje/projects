using Vista.Rendering;

namespace Vista.Tests;

/// <summary>Turning integrator v0's raw GPU sums into the <see cref="Aov"/> layouts.</summary>
[TestClass]
public sealed class IntegratorReadbackTests
{
    [TestMethod]
    public void MeanXyz_DividesBySamplesAndDropsThePadding()
    {
        float[] sums = [4f, 8f, 12f, 99f, 0f, 2f, 6f, -1f];
        CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 0f, 0.5f, 1.5f }, IntegratorReadback.MeanXyz(sums, pixels: 2, samples: 4));
    }

    [TestMethod]
    public void MeanAlbedo_DividesBySamples() =>
        CollectionAssert.AreEqual(new[] { 0.25f, 0.5f, 0.75f }, IntegratorReadback.MeanAlbedo([1f, 2f, 3f, 0f], pixels: 1, samples: 4));

    [TestMethod]
    public void MeanNormal_IsRenormalised_AndAllSkyStaysZero()
    {
        float[] normals = IntegratorReadback.MeanNormal([0f, 3f, 4f, 0f, 0f, 0f, 0f, 0f], pixels: 2);
        CollectionAssert.AreEqual(new[] { 0f, 0.6f, 0.8f, 0f, 0f, 0f }, normals);
    }

    [TestMethod]
    public void Variance_IsTheVarianceOfTheMean()
    {
        float[] samples = [0.2f, 0.9f, 0.4f, 0.4f, 1.3f, 0.1f];
        // The shader's Welford update, in sample order.
        float mean = 0f, m2 = 0f;
        for (int s = 0; s < samples.Length; s++)
        {
            float delta = samples[s] - mean;
            mean += delta / (s + 1);
            m2 += delta * (samples[s] - mean);
        }

        double average = samples.Average(v => (double)v);
        double sampleVariance = samples.Sum(v => (v - average) * (v - average)) / (samples.Length - 1);
        float[] variance = IntegratorReadback.Variance([mean, m2], pixels: 1, samples: samples.Length);
        Assert.AreEqual(sampleVariance / samples.Length, variance[0], 1e-6);
    }

    [TestMethod]
    public void Variance_OfOneSample_IsZero() =>
        CollectionAssert.AreEqual(new[] { 0f }, IntegratorReadback.Variance([0.7f, 0f], pixels: 1, samples: 1));

    [TestMethod]
    public void FirstHit_SplitsDepthAndMaterial()
    {
        float[] firstHit = [12.5f, 0f, float.PositiveInfinity, -1f];
        CollectionAssert.AreEqual(new[] { 12.5f, float.PositiveInfinity }, IntegratorReadback.Depth(firstHit, pixels: 2));
        CollectionAssert.AreEqual(new[] { 0f, -1f }, IntegratorReadback.MaterialId(firstHit, pixels: 2));
    }

    [TestMethod]
    public void WrongSizes_FailLoudly()
    {
        Assert.ThrowsExactly<ArgumentException>(() => IntegratorReadback.MeanXyz(new float[7], pixels: 2, samples: 1));
        Assert.ThrowsExactly<ArgumentException>(() => IntegratorReadback.Depth(new float[3], pixels: 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => IntegratorReadback.MeanXyz(new float[4], pixels: 1, samples: 0));
    }
}
