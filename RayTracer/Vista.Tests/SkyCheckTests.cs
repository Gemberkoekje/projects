using System.Numerics;
using Vista.Atmosphere;

namespace Vista.Tests;

/// <summary>The aggregation behind <c>--atmosphere-check</c> (vista-plan §10 M1.3 verify).</summary>
[TestClass]
public sealed class SkyCheckTests
{
    private const int Chunks = 4;
    private const int Paths = 10;

    private static float[] Wavelengths() => [.. Enumerable.Range(0, 50).Select(i => 419f + i * 4.7f)];

    /// <summary>Raw sums where every path returns <paramref name="reference"/> and the LUT says <paramref name="lut"/>.</summary>
    private static float[] Sums(Func<int, int, float> lut, Func<int, int, float> reference)
    {
        int directions = SkyCheck.Directions().Count;
        var sums = new float[directions * 50 * Chunks * 4];
        for (int d = 0; d < directions; d++)
        {
            for (int l = 0; l < 50; l++)
            {
                for (int c = 0; c < Chunks; c++)
                {
                    int at = ((d * 50 + l) * Chunks + c) * 4;
                    float v = reference(d, l);
                    sums[at] = v * Paths;
                    sums[at + 1] = v * v * Paths;
                    sums[at + 2] = lut(d, l);
                }
            }
        }
        return sums;
    }

    [TestMethod]
    public void Directions_AreUnitAndRelativeToTheSun()
    {
        IReadOnlyList<(double Elevation, double Azimuth, Vector3 Direction)> directions = SkyCheck.Directions();
        Assert.AreEqual(SkyCheck.ViewElevations.Count * SkyCheck.ViewAzimuths.Count, directions.Count);
        foreach ((double e, double a, Vector3 d) in directions)
        {
            Assert.AreEqual(1f, d.Length(), 1e-5f);
            Assert.AreEqual(Math.Sin(e * Math.PI / 180.0), d.Y, 1e-6);
        }
        Vector3 sun = SkyCheck.SunDirection(30.0);
        (_, _, Vector3 toward) = directions.First(x => x.Azimuth == 0.0 && x.Elevation == 30.0);
        Assert.AreEqual(1f, Vector3.Dot(sun, toward), 1e-6f, "azimuth 0 at the sun's elevation is the sun");
    }

    [TestMethod]
    public void IdenticalSkies_HaveNoError()
    {
        float[] sums = Sums((d, l) => 0.01f * (l + 1), (d, l) => 0.01f * (l + 1));
        SkyCheckElevation e = SkyCheck.Aggregate(5.0, sums, Chunks, Paths, Wavelengths(), Enumerable.Repeat(1f, 50).ToArray());
        Assert.AreEqual(Chunks * Paths, e.PathsPerValue);
        Assert.IsTrue(e.Bands.All(b => b.MeanAbsoluteError < 1e-6 && b.MeanStandardError < 1e-3), "no error; the noise is float rounding");
        Assert.AreEqual(0.0, e.MeanAbsoluteLuminanceError, 1e-6);
    }

    [TestMethod]
    public void ABiasInOneBand_ShowsInThatBandOnly()
    {
        float[] wavelengths = Wavelengths();
        float[] sums = Sums((d, l) => wavelengths[l] < 460f ? 0.011f : 0.01f, (d, l) => 0.01f);
        SkyCheckElevation e = SkyCheck.Aggregate(-2.0, sums, Chunks, Paths, wavelengths, Enumerable.Repeat(1f, 50).ToArray());
        SkyCheckBand blue = e.Bands.Single(b => b.Band.StartsWith("400", StringComparison.Ordinal));
        Assert.AreEqual(0.1, blue.MeanAbsoluteError, 1e-4);
        Assert.AreEqual(0.1, blue.WorstError, 1e-4);
        Assert.IsTrue(e.Bands.Where(b => b != blue).All(b => b.MeanAbsoluteError < 1e-5));
    }

    [TestMethod]
    public void ABandWithNoLightAnywhere_IsLeftOut()
    {
        // Single scattering alone with the observer in the Earth's shadow: the blue band's reference is zero everywhere.
        float[] wavelengths = Wavelengths();
        float[] sums = Sums((d, l) => 0.01f, (d, l) => wavelengths[l] < 460f ? 0f : 0.01f);
        SkyCheckElevation e = SkyCheck.Aggregate(-2.0, sums, Chunks, Paths, wavelengths, Enumerable.Repeat(1f, 50).ToArray());
        Assert.IsFalse(e.Bands.Any(b => b.Band.StartsWith("400", StringComparison.Ordinal)));
        Assert.AreEqual(4, e.Bands.Count);
    }
}
