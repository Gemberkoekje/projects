using System.Numerics;
using Vista.Atmosphere;
using Vista.Output;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>
/// The incident meter's maths (vista-plan §10 M1.10a): the card faces the sun and never the ground, its sky is integrated over its
/// hemisphere and its sun through the air, and the exposure puts an 18% grey card at middle grey. The skies here are synthetic sky
/// views, so the integrals have closed forms.
/// </summary>
[TestClass]
public sealed class IncidentMeterTests
{
    private const double Ground = 1000.0;

    private static Vector3 SunAt(double elevationDegrees)
    {
        double e = elevationDegrees * Math.PI / 180.0;
        return new Vector3((float)Math.Cos(e), (float)Math.Sin(e), 0f);
    }

    /// <summary>A frame with the camera a metre above the ground sphere (its horizon dips 0.03°) and the sun at <paramref name="elevationDegrees"/>.</summary>
    private static AtmosphereFrame Frame(double elevationDegrees) =>
        AtmosphereFrame.For(AtmosphereModel.Default, Ground, SunAt(elevationDegrees), cameraY: 1.0, terrainLowY: 0.0, terrainHighY: 2000.0);

    /// <summary>A sky view whose camera slice is <paramref name="above"/> over the horizon and <paramref name="below"/> under it, without haze.</summary>
    private static float[] SkyView(float above, float below)
    {
        var sky = new float[AtmosphereLuts.SkyViewFloats];
        for (int row = 0; row < AtmosphereLuts.SkyRows; row++)
        {
            for (int column = 0; column < AtmosphereLuts.SkyAzimuthSize; column++)
            {
                for (int lambda = 0; lambda < AtmosphereLuts.Wavelengths; lambda++)
                    sky[((row * AtmosphereLuts.SkyAzimuthSize + column) * AtmosphereLuts.Wavelengths + lambda) * 2] = row < AtmosphereLuts.SkyRowsAbove ? above : below;
            }
        }
        return sky;
    }

    /// <summary>The sky's irradiance on the card at wavelength 0 for a top-of-atmosphere irradiance of 1, as the kernel's bands add up.</summary>
    private static double SkyIrradiance(float[] sky, AtmosphereFrame frame)
    {
        double sum = 0.0;
        for (int band = 0; band < SkyDistribution.ZenithBands; band++)
            sum += IncidentMeter.CellSolidAngle(band) * IncidentMeter.BandSky(sky, frame, 1f, 0, band);
        return sum;
    }

    [TestMethod]
    public void TheCard_FacesTheSun_ButNeverTheGround()
    {
        Assert.AreEqual(30.0, IncidentMeter.CardElevationDegrees(SunAt(30.0)), 1e-5);
        Assert.AreEqual(4.0, IncidentMeter.CardElevationDegrees(SunAt(4.0)), 1e-5);
        Assert.AreEqual(0.0, IncidentMeter.CardElevationDegrees(SunAt(-6.0)), "the sun below the horizon: the card stands upright");
        Assert.AreEqual((1.0, 0.0), IncidentMeter.Card(Vector3.UnitY), "the sun overhead: the card lies flat");
        (double cos, double sin) = IncidentMeter.Card(SunAt(30.0));
        Assert.AreEqual(1.0, IncidentMeter.CardCosine(cos, sin, Math.Sin(30.0 * Math.PI / 180.0), 0.0), 1e-12, "toward the sun");
        Assert.AreEqual(0.0, IncidentMeter.CardCosine(cos, sin, 0.0, Math.PI), "behind the card");
    }

    [TestMethod]
    public void AUniformSky_GivesPiTimesItsRadiance_WhereverTheCardFaces()
    {
        float[] sky = SkyView(1f, 1f);
        foreach (double elevation in new[] { 90.0, 30.0, 4.0, -3.0 })
            Assert.AreEqual(Math.PI, SkyIrradiance(sky, Frame(elevation)), Math.PI * 1e-3, $"sun at {elevation}°");
    }

    [TestMethod]
    public void TheGroundBelowTheHorizon_LightsAnUprightCardOnly()
    {
        // Sky above the horizon only: a flat card sees all of it (π), an upright one half (π/2, and the sliver of the dip).
        float[] sky = SkyView(1f, 0f);
        Assert.AreEqual(Math.PI, SkyIrradiance(sky, Frame(90.0)), Math.PI * 1e-3);
        Assert.AreEqual(Math.PI / 2.0, SkyIrradiance(sky, Frame(-3.0)), Math.PI / 2.0 * 2e-3);
        Assert.AreEqual(Math.PI / 2.0, SkyIrradiance(SkyView(0f, 1f), Frame(-3.0)), Math.PI / 2.0 * 2e-3, "and the ground the other half");
    }

    [TestMethod]
    public void TheSun_LightsTheCardThroughTheAir_UntilItSets()
    {
        var clear = new float[AtmosphereLuts.ColumnsFloats]; // no air at all: transmittance 1
        AtmosphereCoefficients k = AtmosphereModel.Default.CoefficientsAt(550.0);
        Assert.AreEqual(2.0, IncidentMeter.SunOnCard(clear, Frame(20.0), k, 2f), 1e-6, "facing the sun, the whole disk");
        Assert.AreEqual(0.0, IncidentMeter.SunOnCard(clear, Frame(-2.0), k, 2f), "set: the Earth's shadow");

        // The centre 0.1° down, the horizon 0.03° down: part of the disk still shines, along the horizon.
        AtmosphereFrame setting = Frame(-0.1);
        (double visible, _) = AtmosphereLuts.SunVisibility(setting, 1.0, setting.SunCosZenith);
        double partial = IncidentMeter.SunOnCard(clear, setting, k, 2f);
        Assert.IsTrue(visible is > 0.1 and < 0.9, $"{visible} of the disk");
        Assert.AreEqual(2.0 * visible * Math.Cos(0.1 * Math.PI / 180.0), partial, 1e-6);
    }

    [TestMethod]
    public void TheKernelsBands_AddUpByTheirCellsSolidAngle()
    {
        var floats = new float[IncidentMeter.Floats];
        for (int item = 0; item < IncidentMeter.Items; item++)
        {
            int band = item % IncidentMeter.ItemsPerGroup;
            for (int lane = 0; lane < IncidentMeter.WavelengthsPerItem; lane++)
                floats[item * 4 + lane] = band == SkyDistribution.ZenithBands ? 5f + lane : 1f;
        }
        IncidentLight light = IncidentMeter.FromKernel(floats);
        double sphere = Enumerable.Range(0, SkyDistribution.ZenithBands).Sum(IncidentMeter.CellSolidAngle) * SkyDistribution.AzimuthBands;
        Assert.AreEqual(4.0 * Math.PI, sphere, 1e-9, "the cells tile the sphere");
        Assert.AreEqual(AtmosphereLuts.Wavelengths, light.Sky.Length);
        Assert.AreEqual(4.0 * Math.PI / SkyDistribution.AzimuthBands, light.Sky[17], 1e-9, "each band item is a sum over one band's columns");
        CollectionAssert.AreEqual(new[] { 5.0, 6.0, 7.0, 8.0, 5.0 }, light.Sun.Take(5).ToArray(), "the sun after the bands, four wavelengths an item");
        Assert.ThrowsExactly<ArgumentException>(() => IncidentMeter.FromKernel(new float[12]));
    }

    [TestMethod]
    public void TheExposure_PutsTheGreyCardAtMiddleGrey()
    {
        IntegratorTables tables = IntegratorTables.Build(MaterialCatalog.M0);
        double[] Flat(double v) => [.. Enumerable.Repeat(v, AtmosphereLuts.Wavelengths)];
        Assert.AreEqual(1.0, IncidentMeter.Luminous(Flat(1.0), tables), 1e-6, "a flat spectrum's Y is its value, as in the image");

        ExposureInfo day = IncidentMeter.Measure(new IncidentLight(Flat(3.0), Flat(1.0)), Frame(30.0), tables, 1.5);
        IncidentReading card = day.Incident!;
        Assert.AreEqual(Metering.Incident, day.Meter);
        Assert.AreEqual(4.0, card.IlluminanceY, 1e-5);
        Assert.AreEqual(4.0 * IntegratorTables.LuminancePerY, card.IlluminanceLux, 1e-1);
        Assert.AreEqual(3.0 / 4.0, card.SunLux / card.IlluminanceLux, 1e-9);
        Assert.AreEqual(Math.Log2(Math.PI / card.IlluminanceY), day.MeteredEv!.Value, 1e-12);
        Assert.AreEqual(day.MeteredEv.Value + 1.5, day.AppliedEv, 1e-12);
        Assert.AreEqual(Math.PI / card.IlluminanceY, day.MeterScale(), 1e-12);
        Assert.AreEqual(Math.Log2(card.IlluminanceLux * 100.0 / IncidentMeter.Calibration), day.Ev100!.Value, 1e-12);
        Assert.AreEqual(30.0, card.CardElevationDegrees, 1e-5);
        Assert.AreEqual(Ground + 1.0, card.AltitudeMetres, 1e-9);

        // A grey card of albedo 0.18 under that light has radiance 0.18·E/π: exposed, it is 0.18 (before the compensation).
        double greyCard = 0.18 * card.IlluminanceY / Math.PI;
        Assert.AreEqual(0.18, greyCard * Math.Pow(2.0, day.MeteredEv.Value), 1e-12);

        ExposureInfo dusk = IncidentMeter.Measure(new IncidentLight(Flat(0.0), Flat(0.01)), Frame(-4.0), tables, 0.0);
        Assert.AreEqual(0.0, dusk.Incident!.SunLux, "only the sky's light is left");
        Assert.AreEqual(0.0, dusk.Incident.CardElevationDegrees);
        Assert.AreEqual(Math.Log2(400.0), dusk.AppliedEv - (day.AppliedEv - 1.5), 1e-5, "400 times less light, 8.6 stops more exposure");

        ExposureInfo night = IncidentMeter.Measure(new IncidentLight(Flat(0.0), Flat(0.0)), Frame(-40.0), tables, 0.5);
        Assert.AreEqual(IncidentMeter.BlackMethodName, night.Method);
        Assert.IsNull(night.MeteredEv);
        Assert.IsNull(night.Ev100);
        Assert.AreEqual(0.5, night.AppliedEv);
        Assert.IsTrue(double.IsNaN(night.MeterScale()));
    }
}
