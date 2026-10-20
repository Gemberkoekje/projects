using Vista.Output;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// The denoiser's measurements (vista-plan §10 M1.6): relMSE against a reference rendered as two halves, with the reference's own
/// noise taken out; the display error; the change in mean brightness; the equal-error sample count; and the sheet's magnified loupe.
/// </summary>
[TestClass]
public sealed class ImageErrorTests
{
    [TestMethod]
    public void RelativeMse_OfKnownValues()
    {
        RelativeError exact = ImageError.RelativeMse([0.2f], [0.1f], [0.1f], 1.0);
        Assert.AreEqual(0.5, exact.Measured, 1e-6, "0.1² / (0.1² + 0.01)");
        Assert.AreEqual(0.0, exact.ReferenceNoise);
        Assert.AreEqual(0.5, exact.Corrected, 1e-6);

        RelativeError noisy = ImageError.RelativeMse([0.2f], [0.15f], [0.05f], 1.0);
        Assert.AreEqual(0.5, noisy.Measured, 1e-6, "against the halves' mean, 0.1");
        Assert.AreEqual(0.125, noisy.ReferenceNoise, 1e-6, "(a − b)²/4 over the same denominator");
        Assert.AreEqual(0.375, noisy.Corrected, 1e-6);

        Assert.AreEqual(0.5, ImageError.RelativeMse([2f], [1f], [1f], 0.1).Measured, 1e-6, "the exposure scales before the ε");
        Assert.AreEqual(new RelativeError(0, 0, 0), ImageError.RelativeMse([0.3f, 0f], [0.3f, 0f], [0.3f, 0f], 1.0));
    }

    [TestMethod]
    public void RelativeMse_TakesTheReferencesNoiseOut()
    {
        // An image with noise σ = 0.02 around the truth, and a reference whose halves each carry σ = 0.03: the corrected error is
        // the image's own, though the reference's noise is larger than it.
        var random = new Random(16);
        const int n = 200_000;
        float[] image = new float[n], a = new float[n], b = new float[n];
        double expected = 0.0;
        for (int i = 0; i < n; i++)
        {
            double truth = 0.1 + 0.05 * Math.Sin(i * 0.001);
            image[i] = (float)(truth + 0.02 * Gaussian(random));
            a[i] = (float)(truth + 0.03 * Gaussian(random));
            b[i] = (float)(truth + 0.03 * Gaussian(random));
            expected += 0.02 * 0.02 / (truth * truth + ImageError.RelativeEpsilon);
        }
        expected /= n;
        RelativeError error = ImageError.RelativeMse(image, a, b, 1.0);
        Assert.AreEqual(expected, error.Corrected, 0.05 * expected);
        Assert.IsTrue(error.Measured > 2 * expected, "uncorrected, the reference's noise would dominate");
        Assert.AreEqual(error.Measured - error.Corrected, error.ReferenceNoise, 1e-12);
    }

    [TestMethod]
    public void RelativeMse_NeedsImagesOfOneSize()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ImageError.RelativeMse([1f], [1f, 2f], [1f], 1.0));
        Assert.ThrowsExactly<ArgumentException>(() => ImageError.RelativeMse([], [], [], 1.0));
    }

    [TestMethod]
    public void DisplayRmse_MeanBias_AndTheEqualErrorSamples()
    {
        Assert.AreEqual(Math.Sqrt(50.0), ImageError.DisplayRmse([0, 10], [0, 0]), 1e-12);
        Assert.ThrowsExactly<ArgumentException>(() => ImageError.DisplayRmse([0], [0, 0]));

        Assert.AreEqual(0.1, ImageError.MeanLuminanceBias([1.1f, 2.2f, 3.3f], [1f, 2f, 3f]), 1e-6);
        Assert.AreEqual(0.0, ImageError.MeanLuminanceBias([1f, 1f, 1f], [0f, 0f, 0f]), "no light in the reference");
        Assert.ThrowsExactly<ArgumentException>(() => ImageError.MeanLuminanceBias([1f, 1f], [1f, 1f]));

        Assert.AreEqual(160.0, ImageError.EqualErrorSpp(16, 0.005, 0.0005), 1e-9, "ten times less error: ten times the samples");
        Assert.AreEqual(double.PositiveInfinity, ImageError.EqualErrorSpp(16, 0.005, 0.0));
    }

    [TestMethod]
    public void MaxRelativeDifference_AndBitIdentity()
    {
        Assert.AreEqual(0.1 / 1.1, ImageError.MaxRelativeDifference([1f, 0f, -2f], [1.1f, 0f, -2f]), 1e-6);
        Assert.AreEqual(0.0, ImageError.MaxRelativeDifference([0f], [0f]));
        Assert.IsTrue(ImageError.BitIdentical([1f, float.NaN], [1f, float.NaN]));
        Assert.IsFalse(ImageError.BitIdentical([0f], [-0f]), "equal values, different bits");
        Assert.IsFalse(ImageError.BitIdentical([1f], [1f, 1f]));
    }

    [TestMethod]
    public void Magnify_RepeatsEachPixelAsASquare()
    {
        byte[] big = ContactSheet.Magnify([1, 2, 3, 4, 5, 6], 2, 1, 2);
        byte[] row = [1, 2, 3, 1, 2, 3, 4, 5, 6, 4, 5, 6];
        CollectionAssert.AreEqual(row.Concat(row).ToArray(), big);
        CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, ContactSheet.Magnify([9, 8, 7], 1, 1, 1));
        Assert.ThrowsExactly<ArgumentException>(() => ContactSheet.Magnify([1, 2, 3], 2, 1, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ContactSheet.Magnify([1, 2, 3], 1, 1, 0));
    }

    private static double Gaussian(Random random) =>
        Math.Sqrt(-2.0 * Math.Log(1.0 - random.NextDouble())) * Math.Cos(2.0 * Math.PI * random.NextDouble());
}
