using Vista.Output;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// The PNG's finish (vista-plan §10 M1.11c): the triangular dither before the 8-bit quantisation — deterministic, zero-mean, keyed by
/// the frame pixel so a loupe stays a crop, off writing M1.11b's bytes — and the 16-bit PNG through the same exposure and curve.
/// </summary>
[TestClass]
public sealed class OutputFinishTests
{
    /// <summary>A frame of linear Rec.709 with smooth gradients, saturated colours and the extremes, <paramref name="width"/> wide.</summary>
    private static float[] Frame(int width, int height)
    {
        var rgb = new float[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 3;
                float t = (float)x / width;
                rgb[p] = 0.02f + 0.3f * t * t;
                rgb[p + 1] = 0.05f + 0.2f * t + 0.01f * y;
                rgb[p + 2] = (x + y) % 11 == 0 ? 0f : (x + y) % 13 == 0 ? 500f : 0.12f + 0.04f * y;
            }
        }
        return rgb;
    }

    [TestMethod]
    public void DitherOff_WritesM1_11bsBytes()
    {
        float[] frame = Frame(40, 24);
        foreach (DisplayCurve curve in new[] { DisplayCurve.Agx, DisplayCurve.Aces })
        {
            CollectionAssert.AreEqual(DisplayTransform.ToSrgb8(frame, 0.7, curve, DisplayLook.Punchy),
                DisplayTransform.ToSrgb8(frame, 0.7, curve, DisplayLook.Punchy, PngDither.Off, PixelWindow.Full(40, 24)), curve.ToString());
        }
        CollectionAssert.AreNotEqual(DisplayTransform.ToSrgb8(frame, 0.7, DisplayCurve.Agx, DisplayLook.Punchy),
            DisplayTransform.ToSrgb8(frame, 0.7, DisplayCurve.Agx, DisplayLook.Punchy, PngDither.Triangular, PixelWindow.Full(40, 24)));
        Assert.AreEqual(PngDither.Triangular, DisplayTransform.DitherOf(8, PngDither.None), "none asked for is the default");
        Assert.AreEqual(PngDither.Off, DisplayTransform.DitherOf(8, PngDither.Off));
        Assert.AreEqual(PngDither.None, DisplayTransform.DitherOf(16, PngDither.Triangular), "a 16-bit PNG needs none");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DisplayTransform.DitherOf(12, PngDither.Off));
    }

    [TestMethod]
    public void TheDither_IsDeterministic_AndALoupeIsACropOfTheFrame()
    {
        const int width = 40, height = 24;
        float[] frame = Frame(width, height);
        byte[] full = DisplayTransform.ToSrgb8(frame, 0.0, DisplayCurve.Agx, DisplayLook.Punchy, PngDither.Triangular, PixelWindow.Full(width, height));
        CollectionAssert.AreEqual(full, DisplayTransform.ToSrgb8(frame, 0.0, DisplayCurve.Agx, DisplayLook.Punchy, PngDither.Triangular, PixelWindow.Full(width, height)));

        var window = new PixelWindow(5, 7, 13, 9);
        byte[] loupe = DisplayTransform.ToSrgb8(window.Crop(frame, width, 3), 0.0, DisplayCurve.Agx, DisplayLook.Punchy, PngDither.Triangular, window);
        for (int y = 0; y < window.Height; y++)
        {
            for (int i = 0; i < window.Width * 3; i++)
                Assert.AreEqual(full[((window.Y + y) * width + window.X) * 3 + i], loupe[y * window.Width * 3 + i], $"row {y}, byte {i}");
        }
        Assert.ThrowsExactly<ArgumentException>(() => DisplayTransform.ToSrgb8(frame, 0.0, DisplayCurve.Agx, DisplayLook.Punchy, PngDither.Triangular, window));
    }

    [TestMethod]
    public void TheDither_IsTriangularAndZeroMean()
    {
        // The offsets of a 256×256 frame: on (−1, 1), mean 0, variance 1/6 (a triangle's), each channel its own.
        double sum = 0.0, squares = 0.0, cross = 0.0;
        int n = 0;
        for (int y = 0; y < 256; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                float r = DisplayTransform.DitherOffset(x, y, 0);
                float g = DisplayTransform.DitherOffset(x, y, 1);
                Assert.IsTrue(r > -1f && r < 1f, $"({x}, {y}): {r}");
                sum += r;
                squares += r * r;
                cross += r * g;
                n++;
            }
        }
        Assert.AreEqual(0.0, sum / n, 0.01);
        Assert.AreEqual(1.0 / 6.0, squares / n, 0.005);
        Assert.AreEqual(0.0, cross / n, 0.01, "the channels' offsets are independent");
    }

    [TestMethod]
    public void AFlatField_KeepsItsLevelOnAverage_AndIsNoLongerOneStep()
    {
        // A flat 64×64 field between two 8-bit steps: rounded, every pixel is the nearer step; dithered, they spread over three
        // steps around it, and their mean is the field's level (a TPDF dither's error has no mean).
        const int side = 64;
        foreach (float linear in new[] { 0.0021f, 0.05f, 0.2f, 0.61f })
        {
            float[] field = [.. Enumerable.Repeat(linear, side * side * 3)];
            byte[] rounded = DisplayTransform.ToSrgb8(field, 0.0, DisplayCurve.Aces, DisplayLook.None);
            byte[] dithered = DisplayTransform.ToSrgb8(field, 0.0, DisplayCurve.Aces, DisplayLook.None, PngDither.Triangular, PixelWindow.Full(side, side));
            float level = DisplayTransform.Encode([linear, linear, linear], 0.0, DisplayCurve.Aces, DisplayLook.None)[0] * 255f;
            Assert.AreEqual(1, rounded.Distinct().Count(), $"{linear}: rounding gives one step");
            Assert.IsTrue(dithered.Distinct().Count() >= 2, $"{linear}: the dither spreads it");
            Assert.IsTrue(dithered.All(b => Math.Abs(b - level) < 1.5f), $"{linear}: within a step and a half of {level}");
            Assert.AreEqual(level, dithered.Average(b => (double)b), 0.04, $"{linear}: the mean is the level");
        }
    }

    [TestMethod]
    public void BlackAndWhite_StayExact()
    {
        byte[] bytes = DisplayTransform.ToSrgb8([0f, 0f, 0f, 1e4f, 1e4f, 1e4f, float.NaN, -1f, 0f], 0.0, DisplayCurve.Aces, DisplayLook.None,
            PngDither.Triangular, PixelWindow.Full(3, 1));
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 255, 255, 255, 0, 0, 0 }, bytes);
    }

    [TestMethod]
    public void The16BitPng_IsTheSameCurve_At257TimesTheSteps()
    {
        float[] frame = Frame(40, 24);
        byte[] eight = DisplayTransform.ToSrgb8(frame, 0.3, DisplayCurve.Agx, DisplayLook.Punchy);
        ushort[] sixteen = DisplayTransform.ToSrgb16(frame, 0.3, DisplayCurve.Agx, DisplayLook.Punchy);
        Assert.AreEqual(eight.Length, sixteen.Length);
        for (int i = 0; i < eight.Length; i++)
            Assert.IsTrue(Math.Abs(sixteen[i] - eight[i] * 257) <= 129, $"sample {i}: {sixteen[i]} against {eight[i]}");
        Assert.IsTrue(sixteen.Distinct().Count() > 2 * eight.Distinct().Count(), "finer steps");
        CollectionAssert.AreEqual(new ushort[] { 0, 0, 0, 65535, 65535, 65535 },
            DisplayTransform.ToSrgb16([0f, -1f, float.NaN, 1e4f, 1e4f, 1e4f], 0.0, DisplayCurve.Aces, DisplayLook.None));
    }
}
