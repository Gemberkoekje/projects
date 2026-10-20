using Vista.Output;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>The PNG's metered exposure (vista-plan §10 M1 decision "physical units").</summary>
[TestClass]
public sealed class AutoExposureTests
{
    private static float[] Uniform(int pixels, float y)
    {
        var xyz = new float[pixels * 3];
        for (int p = 0; p < pixels; p++)
            (xyz[p * 3], xyz[p * 3 + 1], xyz[p * 3 + 2]) = (0.95f * y, y, 1.09f * y);
        return xyz;
    }

    [TestMethod]
    public void UniformImage_MapsToMiddleGrey()
    {
        ExposureInfo e = AutoExposure.Measure(Uniform(100, 0.05f), 0.0);
        Assert.AreEqual(0.05, e.LogAverageY, 1e-6);
        Assert.AreEqual(Math.Log2(0.18 / 0.05), e.AppliedEv, 1e-6);
        Assert.AreEqual(0.05 * IntegratorTables.LuminancePerY, e.LogAverageLuminanceCdM2, 1e-3);
        Assert.AreEqual(AutoExposure.MethodName, e.Method);
    }

    [TestMethod]
    public void Compensation_AddsStops()
    {
        ExposureInfo plain = AutoExposure.Measure(Uniform(100, 0.05f), 0.0);
        ExposureInfo bright = AutoExposure.Measure(Uniform(100, 0.05f), 1.5);
        Assert.AreEqual(plain.AppliedEv + 1.5, bright.AppliedEv, 1e-9);
        Assert.AreEqual(plain.Ev100, bright.Ev100, "the scene's EV does not change");
    }

    [TestMethod]
    public void TheSunsDisk_DoesNotDecideTheExposure()
    {
        float[] sky = Uniform(1000, 0.02f);
        sky[1] = 3e4f; // one pixel of sun
        ExposureInfo e = AutoExposure.Measure(sky, 0.0);
        Assert.AreEqual(0.02, e.LogAverageY, 0.02 * 1e-6, "the brightest 1% is clamped to its percentile");
    }

    [TestMethod]
    public void DaylightAndTwilight_MeterAboutTwelveStopsApart()
    {
        ExposureInfo day = AutoExposure.Measure(Uniform(10, 0.03f), 0.0);
        ExposureInfo twilight = AutoExposure.Measure(Uniform(10, 0.03f / 4096f), 0.0);
        Assert.AreEqual(12.0, day.Ev100!.Value - twilight.Ev100!.Value, 1e-6);
    }

    [TestMethod]
    public void BlackImage_KeepsOnlyTheCompensation()
    {
        ExposureInfo e = AutoExposure.Measure(new float[30], 0.5);
        Assert.AreEqual(AutoExposure.BlackMethodName, e.Method);
        Assert.IsNull(e.Ev100);
        Assert.AreEqual(0.5, e.AppliedEv);
    }

    [TestMethod]
    public void Meter_IsTheWholeFrameAtPreviewSize()
    {
        var image = new RenderSettings { Width = 3840, Height = 1600, SamplesPerPixel = 1024, MaxBounces = 9, Window = new PixelWindow(100, 100, 64, 64), Atmosphere = AtmosphereMode.Reference };
        RenderSettings meter = AutoExposure.MeterSettings(image);
        Assert.AreEqual((320, 133), (meter.Width, meter.Height), "the frame's aspect ratio");
        Assert.IsTrue(meter.Window.IsEmpty, "the whole frame, also for a loupe");
        Assert.AreEqual(AutoExposure.MeterSamplesPerPixel, meter.SamplesPerPixel);
        Assert.AreEqual(9, meter.MaxBounces);
        Assert.IsTrue(meter.ExposureMeter);
        Assert.AreEqual(AtmosphereMode.Luts, meter.Atmosphere, "a reference image is metered on the LUT sky");
        Assert.IsFalse(image.ExposureMeter);
    }
}
