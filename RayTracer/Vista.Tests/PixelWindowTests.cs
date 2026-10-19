using Vista.Recipes;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>The loupe's window of the frame (vista-plan §7.4, M1.1).</summary>
[TestClass]
public sealed class PixelWindowTests
{
    [TestMethod]
    [DataRow("1700,800,320,180", 1700, 800, 320, 180)]
    [DataRow(" 0, 0 ,1,1", 0, 0, 1, 1)]
    public void TryParse_ReadsXYWidthHeight(string text, int x, int y, int w, int h)
    {
        Assert.IsTrue(PixelWindow.TryParse(text, out PixelWindow window));
        Assert.AreEqual(new PixelWindow(x, y, w, h), window);
        Assert.IsTrue(PixelWindow.TryParse(window.ToString(), out PixelWindow back));
        Assert.AreEqual(window, back);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("1,2,3")]
    [DataRow("1,2,3,4,5")]
    [DataRow("-1,0,4,4")]
    [DataRow("0,0,0,4")]
    [DataRow("0,0,4,0")]
    [DataRow("a,0,4,4")]
    public void TryParse_RejectsMalformedWindows(string text) =>
        Assert.IsFalse(PixelWindow.TryParse(text, out _));

    [TestMethod]
    public void FitsIn_ChecksEveryEdge()
    {
        Assert.IsTrue(new PixelWindow(0, 0, 32, 18).FitsIn(32, 18));
        Assert.IsTrue(new PixelWindow(31, 17, 1, 1).FitsIn(32, 18));
        Assert.IsFalse(new PixelWindow(31, 0, 2, 1).FitsIn(32, 18));
        Assert.IsFalse(new PixelWindow(0, 17, 1, 2).FitsIn(32, 18));
        Assert.IsFalse(new PixelWindow(-1, 0, 1, 1).FitsIn(32, 18));
        Assert.IsFalse(default(PixelWindow).FitsIn(32, 18), "an empty window renders nothing");
        Assert.IsFalse(new PixelWindow(int.MaxValue, 0, 1, 1).FitsIn(32, 18), "no overflow");
    }

    [TestMethod]
    public void Crop_CutsTheWindowOutOfAFrameBuffer()
    {
        // A 5×4 frame, 2 floats per pixel: pixel (x, y) holds (10y + x, −(10y + x)).
        var frame = new float[5 * 4 * 2];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                frame[(y * 5 + x) * 2] = 10 * y + x;
                frame[(y * 5 + x) * 2 + 1] = -(10 * y + x);
            }
        }
        float[] crop = new PixelWindow(1, 2, 3, 2).Crop(frame, 5, 2);
        CollectionAssert.AreEqual(new float[] { 21, -21, 22, -22, 23, -23, 31, -31, 32, -32, 33, -33 }, crop);
        Assert.ThrowsExactly<ArgumentException>(() => new PixelWindow(3, 0, 3, 1).Crop(frame, 5, 2));
    }

    [TestMethod]
    public void RenderSettings_RenderTheWholeFrameUnlessAWindowIsSet()
    {
        var settings = new RenderSettings { Width = 640, Height = 360 };
        Assert.AreEqual(PixelWindow.Full(640, 360), settings.Rendered);
        var loupe = new PixelWindow(291, 137, 64, 64);
        Assert.AreEqual(loupe, (settings with { Window = loupe }).Rendered);
    }

    [TestMethod]
    public void Json_HoldsOnlyThePlaceAndSize()
    {
        string json = VistaJson.Serialize(new PixelWindow(1, 2, 3, 4));
        Assert.AreEqual("{\n  \"x\": 1,\n  \"y\": 2,\n  \"width\": 3,\n  \"height\": 4\n}\n", json);
        Assert.AreEqual(new PixelWindow(1, 2, 3, 4), VistaJson.Deserialize<PixelWindow>(json));
    }
}
