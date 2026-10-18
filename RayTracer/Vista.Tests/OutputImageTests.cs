using Vista.Output;
using Vista.Recipes;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>The display transform and the debug-image encoders (vista-plan §5.7 / §7.4).</summary>
[TestClass]
public sealed class OutputImageTests
{
    [TestMethod]
    public void Srgb_EncodesTheTransferCurve()
    {
        Assert.AreEqual(0, DisplayTransform.EncodeSrgb(0f));
        Assert.AreEqual(0, DisplayTransform.EncodeSrgb(-1f));
        Assert.AreEqual(0, DisplayTransform.EncodeSrgb(float.NaN));
        Assert.AreEqual(255, DisplayTransform.EncodeSrgb(1f));
        Assert.AreEqual(255, DisplayTransform.EncodeSrgb(float.PositiveInfinity));
        Assert.AreEqual(188, DisplayTransform.EncodeSrgb(0.5f)); // 0.7354 × 255
        Assert.AreEqual(13, DisplayTransform.EncodeSrgb(0.004f)); // linear toe: 12.92 × 0.004 × 255
    }

    [TestMethod]
    public void Aces_IsMonotonicAndBounded()
    {
        float previous = 0f;
        for (float x = 0.001f; x < 100f; x *= 1.3f)
        {
            float y = DisplayTransform.Aces(x);
            Assert.IsTrue(y >= previous && y <= 1f);
            previous = y;
        }
        Assert.AreEqual(0f, DisplayTransform.Aces(float.NaN));
    }

    [TestMethod]
    public void Exposure_IsInStops()
    {
        float[] grey = [0.1f, 0.1f, 0.1f];
        byte[] plusOne = DisplayTransform.ToSrgb8(grey, 1.0);
        byte[] doubled = DisplayTransform.ToSrgb8([0.2f, 0.2f, 0.2f], 0.0);
        CollectionAssert.AreEqual(doubled, plusOne);
    }

    [TestMethod]
    public void Xyz_UsesTheEngineMatrix()
    {
        // D65 white (X, Y, Z) = (0.9505, 1, 1.089) maps to linear sRGB white.
        float[] rgb = DisplayTransform.XyzToLinearRec709([0.9505f, 1f, 1.089f]);
        Assert.AreEqual(1f, rgb[0], 2e-3f);
        Assert.AreEqual(1f, rgb[1], 2e-3f);
        Assert.AreEqual(1f, rgb[2], 2e-3f);
    }

    [TestMethod]
    public void Normal_UpIsGreen()
    {
        CollectionAssert.AreEqual(new byte[] { 128, 255, 128 }, DebugImages.Normal([0f, 1f, 0f]));
    }

    [TestMethod]
    public void Depth_NearIsBrightFarIsDarkSkyIsBlue()
    {
        byte[] img = DebugImages.Depth([10f, 10000f, float.PositiveInfinity, 100f]);
        Assert.AreEqual(255, img[0]);
        Assert.AreEqual(32, img[3]);
        Assert.AreEqual((0, 0, 96), (img[6], img[7], img[8]));
        Assert.IsTrue(img[9] < 255 && img[9] > 32);
    }

    [TestMethod]
    public void MaterialAndTier_ColourHitsAndBlackenSky()
    {
        byte[] material = DebugImages.Material([0f, -1f]);
        Assert.AreNotEqual(0, material[0] + material[1] + material[2]);
        Assert.AreEqual(0, material[3] + material[4] + material[5]);

        byte[] tier = DebugImages.Tier([0f, -1f], MaterialCatalog.M0);
        Assert.AreEqual(DebugImages.TierColor(DataTier.BuiltIn), (tier[0], tier[1], tier[2]));
        Assert.AreEqual(0, tier[3] + tier[4] + tier[5]);
    }

    [TestMethod]
    public void Variance_HeatRisesWithNoise()
    {
        byte[] img = DebugImages.Variance([0f, 1e-3f, 1f]);
        Assert.AreEqual(0, img[0] + img[1] + img[2]);
        Assert.AreEqual((255, 255, 255), (img[6], img[7], img[8]));
        Assert.IsTrue(img[3] > 0 && img[3] < 255);
    }

    [TestMethod]
    public void HeightMap_MarksTheCamera()
    {
        HeightField field = TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.RollingHills] }, 2, new WorldGrid(65, 32.0));
        CameraSpec camera = DefaultCamera.For(field);
        (byte[] rgb, int size) = DebugImages.HeightMap(field, camera);
        Assert.AreEqual(65, size);
        Assert.AreEqual(65 * 65 * 3, rgb.Length);
        // Camera at x = 0 (middle column), z = −0.35·extent → row (1 − 0.15)·64 from the top.
        int col = 32, row = (int)Math.Round(64 - 0.15 * 64);
        int p = (row * size + col) * 3;
        Assert.AreEqual((255, 0, 0), (rgb[p], rgb[p + 1], rgb[p + 2]));
        // Heading north draws the marker upward.
        int above = ((row - 3) * size + col) * 3;
        Assert.AreEqual((255, 0, 0), (rgb[above], rgb[above + 1], rgb[above + 2]));
    }
}
