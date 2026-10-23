using System.Numerics;
using Vista.Atmosphere;

namespace Vista.Tests;

/// <summary>
/// The C# references of the atmosphere LUTs (vista-plan §9): the sphere geometry, the texel maps, and the integrands
/// against exact or brute-force values. The GPU's parity with these runs in <c>--gpu-selftest</c>.
/// </summary>
[TestClass]
public sealed class AtmosphereLutsTests
{
    private static readonly AtmosphereModel Model = AtmosphereModel.Default;

    /// <summary>Zermatt's valley floor, the sun 20° up in the south-west.</summary>
    internal static readonly AtmosphereFrame Frame = AtmosphereFrame.For(Model, 1608.0, new Vector3(-0.66f, 0.342f, -0.66f), 100.0, 0.0, 2400.0);

    private static readonly Lazy<float[]> SharedColumns = new(() =>
    {
        var columns = new float[AtmosphereLuts.ColumnsFloats];
        Parallel.For(0, AtmosphereLuts.ColumnsAltitudeSize, j =>
        {
            for (int i = 0; i < AtmosphereLuts.ColumnsMuSize; i++)
            {
                DensityColumns c = AtmosphereLuts.ColumnsTexel(Model, i, j);
                int at = (j * AtmosphereLuts.ColumnsMuSize + i) * 4;
                (columns[at], columns[at + 1], columns[at + 2]) = ((float)c.Rayleigh, (float)c.Aerosol, (float)c.Ozone);
            }
        });
        return columns;
    });

    /// <summary>The whole columns LUT of the default model, built once in C#.</summary>
    internal static float[] Columns => SharedColumns.Value;

    [TestMethod]
    public void AltitudeAlong_IsTheGeometryOfAStraightRay()
    {
        const double radius = 6_361_608.0;
        foreach ((double h0, double mu, double t) in new[] { (1.0, 0.0, 50_000.0), (2000.0, -0.02, 30_000.0), (500.0, 0.7, 1e5), (10.0, -1.0, 9.0) })
        {
            double r0 = radius + h0;
            double exact = Math.Sqrt(r0 * r0 + t * t + 2.0 * r0 * mu * t) - radius;
            Assert.AreEqual(exact, AtmosphereLuts.AltitudeAlong(radius, h0, mu, t), Math.Abs(exact) * 1e-9 + 1e-6);
        }
    }

    [TestMethod]
    public void Sphere_HitsAndDistances()
    {
        const double radius = 6_361_608.0;
        double dip = Math.Acos(radius / (radius + 1000.0)); // the horizon from 1 km up
        Assert.IsTrue(AtmosphereLuts.HitsSphere(radius, 1000.0, -Math.Sin(dip + 1e-4)));
        Assert.IsFalse(AtmosphereLuts.HitsSphere(radius, 1000.0, -Math.Sin(dip - 1e-4)));
        Assert.IsFalse(AtmosphereLuts.HitsSphere(radius, 1000.0, 0.1), "upward rays never hit");
        Assert.AreEqual(1000.0, AtmosphereLuts.DistanceToSphereBelow(radius, 1000.0, -1.0), 1e-6, "straight down");
        Assert.AreEqual(Math.Sqrt(1000.0 * (2.0 * radius + 1000.0)), AtmosphereLuts.DistanceToSphereBelow(radius, 1000.0, -Math.Sin(dip)), 1e-3, "tangent");
        Assert.AreEqual(99_000.0, AtmosphereLuts.DistanceToShellAbove(radius, 1000.0, 100_000.0, 1.0), 1e-6, "straight up");
    }

    [TestMethod]
    public void ColumnsMap_RoundTrips()
    {
        foreach ((double xMu, double xH) in new[] { (0.0, 0.0), (0.3, 0.1), (0.99, 0.5), (1.0, 1.0), (0.5, 0.02) })
        {
            (double h, double mu) = AtmosphereLuts.ColumnsCoordinatesToAltitudeMu(Model, xMu, xH);
            (double backMu, double backH) = AtmosphereLuts.ColumnsAltitudeMuToCoordinates(Model, h, mu);
            Assert.AreEqual(xMu, backMu, 1e-6);
            Assert.AreEqual(xH, backH, 1e-9);
        }
        Assert.AreEqual(1.0, AtmosphereLuts.ColumnsCoordinatesToAltitudeMu(Model, 0.0, 0.3).Mu, 1e-12, "x_μ = 0 is the zenith");
    }

    [TestMethod]
    public void ColumnsTexel_AtTheZenith_IsTheExactColumn()
    {
        for (int j = 0; j < AtmosphereLuts.ColumnsAltitudeSize; j += 51)
        {
            (double h, _) = AtmosphereLuts.ColumnsCoordinatesToAltitudeMu(Model, 0.0, (double)j / (AtmosphereLuts.ColumnsAltitudeSize - 1));
            DensityColumns exact = Model.ZenithColumns(h);
            DensityColumns texel = AtmosphereLuts.ColumnsTexel(Model, 0, j);
            Assert.AreEqual(exact.Rayleigh, texel.Rayleigh, exact.Rayleigh * 1e-4 + 1e-9, $"Rayleigh at {h:0} m");
            Assert.AreEqual(exact.Aerosol, texel.Aerosol, exact.Aerosol * 1e-3 + 1e-9, $"haze at {h:0} m");
            Assert.AreEqual(exact.Ozone, texel.Ozone, exact.Ozone * 1e-3 + 1e-6, $"ozone at {h:0} m");
        }
    }

    [TestMethod]
    public void SampledTransmittance_MatchesAFineIntegration_DownToTheHorizon()
    {
        AtmosphereCoefficients k = Model.CoefficientsAt(450.0);
        foreach ((double altitude, double mu) in new[] { (1608.0, 1.0), (1608.0, 0.3), (3000.0, 0.05), (4000.0, -0.01), (1610.0, 0.002) })
        {
            double fine = Math.Exp(-k.OpticalDepth(AtmosphereLuts.ColumnsAlong(Model, altitude, mu, 20_000)));
            double sampled = Math.Exp(-k.OpticalDepth(AtmosphereLuts.SampleColumns(Columns, Model, altitude, mu)));
            Assert.AreEqual(fine, sampled, Math.Max(fine * 5e-3, 1e-5), $"altitude {altitude}, μ {mu}: {fine} vs {sampled}");
        }
    }

    [TestMethod]
    public void SunTransmittance_IsZeroInTheEarthsShadow_AndRedderLow()
    {
        AtmosphereCoefficients blue = Model.CoefficientsAt(450.0), red = Model.CoefficientsAt(650.0);
        double valley = Frame.GroundAltitudeMetres + 1.0;
        Assert.AreEqual(0.0, AtmosphereLuts.SunTransmittance(Columns, Frame, blue, valley, -0.01), "below the valley's horizon");
        double peak = Frame.GroundAltitudeMetres + 2400.0;
        double mu = -Math.Sin(0.5 * Math.PI / 180.0);
        double peakBlue = AtmosphereLuts.SunTransmittance(Columns, Frame, blue, peak, mu);
        double peakRed = AtmosphereLuts.SunTransmittance(Columns, Frame, red, peak, mu);
        Assert.IsTrue(peakRed > 0.0, "a peak still sees the sun half a degree below the valley's horizon");
        Assert.IsTrue(peakRed > 5.0 * peakBlue, $"and it is red: {peakRed} against {peakBlue}");
    }

    [TestMethod]
    public void StepIntegral_IsSmoothAcrossItsSeries()
    {
        const double dt = 1000.0;
        foreach (double x in new[] { 1e-9, 1e-6, 0.009999, 0.01, 0.0100001, 0.5, 5.0 })
        {
            double extinction = x / dt;
            double exact = OneMinusExpOverX(x) * dt;
            Assert.AreEqual(exact, AtmosphereLuts.StepIntegral(extinction, dt), exact * 1e-7, $"x = {x}");
        }
    }

    /// <summary>(1 − e^(−x))/x without cancellation: its alternating series below 1, directly above.</summary>
    private static double OneMinusExpOverX(double x)
    {
        if (x >= 1.0)
            return (1.0 - Math.Exp(-x)) / x;
        double sum = 0.0, term = 1.0;
        for (int n = 1; n < 30; n++)
        {
            sum += term;
            term *= -x / (n + 1);
        }
        return sum;
    }

    [TestMethod]
    public void SkyRows_EndExactlyOnTheHorizon()
    {
        double radius = Frame.GroundRadius;
        (double horizon, double below) = AtmosphereLuts.Horizon(radius, 100.0);
        Assert.AreEqual((0.0, false), AtmosphereLuts.SkyRowToZenith(radius, 100.0, 0));
        Assert.AreEqual((horizon, false), AtmosphereLuts.SkyRowToZenith(radius, 100.0, AtmosphereLuts.SkyRowsAbove - 1));
        Assert.AreEqual((horizon, true), AtmosphereLuts.SkyRowToZenith(radius, 100.0, AtmosphereLuts.SkyRowsAbove));
        (double nadir, bool ground) = AtmosphereLuts.SkyRowToZenith(radius, 100.0, AtmosphereLuts.SkyRows - 1);
        Assert.AreEqual(Math.PI, nadir, 1e-12);
        Assert.IsTrue(ground);
        Assert.AreEqual(Math.PI, horizon + below, 1e-12);
        Assert.IsTrue(horizon > 0.5 * Math.PI, "the horizon dips below the horizontal");
    }

    [TestMethod]
    public void SingleScatteringSky_MatchesABruteForceMarch()
    {
        // With no multiple scattering (an all-zero orders table), a sky-view texel is single scattering through the
        // columns LUT; a march of 20 000 steps with exactly integrated sun transmittance must agree.
        var orders = new float[AtmosphereScattering.FloatsPerChannel * 2];
        AtmosphereCoefficients k = Model.CoefficientsAt(450.0);
        foreach ((int row, int column) in new[] { (10, 0), (40, 32), (63, 63), (55, 5) })
        {
            (double smooth, double mie) = AtmosphereLuts.SkyViewTexel(Columns, orders, [], Frame, k, 0, 0.0, 0, row, column);
            double height = Frame.SliceAltitude(0) - Frame.GroundAltitudeMetres;
            (double zenith, _) = AtmosphereLuts.SkyRowToZenith(Frame.GroundRadius, height, row);
            double phi = AtmosphereLuts.SkyColumnToAzimuth(column);
            double muS = Frame.SunCosZenith, sinS = Math.Sqrt(1.0 - muS * muS);
            double nu = Math.Sin(zenith) * Math.Cos(phi) * sinS + Math.Cos(zenith) * muS;
            (double bruteRayleigh, double bruteMie) = BruteSingle(k, height, Math.Cos(zenith), muS, nu);
            Assert.AreEqual(bruteRayleigh * AirOptics.RayleighPhase(nu), smooth, smooth * 0.01, $"row {row}, column {column}");
            Assert.AreEqual(bruteMie, mie, mie * 0.01, $"row {row}, column {column}");
        }
    }

    private static (double Rayleigh, double Mie) BruteSingle(AtmosphereCoefficients k, double height, double mu, double muS, double nu)
    {
        double rb = Frame.GroundRadius, top = Frame.TopRadius - rb, r0 = rb + height;
        double length = AtmosphereLuts.DistanceToShellAbove(rb, height, top, mu);
        const int steps = 20_000;
        double dt = length / steps, depth = 0.0, rayleigh = 0.0, mie = 0.0;
        for (int s = 0; s < steps; s++)
        {
            double t = (s + 0.5) * dt;
            double hg = AtmosphereLuts.AltitudeAlong(rb, height, mu, t);
            double rp = rb + hg;
            double muSp = (r0 * muS + t * nu) / rp;
            double altitude = Frame.GroundAltitudeMetres + hg;
            (_, double extinction, double rs, double ms) = AtmosphereLuts.Coefficients(Model, k, altitude);
            double sun = AtmosphereLuts.HitsSphere(rb, hg, muSp) ? 0.0 : Math.Exp(-k.OpticalDepth(AtmosphereLuts.ColumnsAlong(Model, altitude, muSp, 400)));
            double view = Math.Exp(-(depth + 0.5 * extinction * dt));
            rayleigh += view * sun * rs * dt;
            mie += view * sun * ms * dt;
            depth += extinction * dt;
        }
        return (rayleigh, mie);
    }

    [TestMethod]
    public void RelativeAzimuth_IsTheHorizontalAngle()
    {
        Assert.AreEqual(0.0, AtmosphereLuts.RelativeAzimuth(0.0, 1.0, 0.0, 2.0), 1e-12);
        Assert.AreEqual(Math.PI, AtmosphereLuts.RelativeAzimuth(1.0, 0.0, -1.0, 0.0), 1e-12);
        Assert.AreEqual(0.5 * Math.PI, AtmosphereLuts.RelativeAzimuth(1.0, 0.0, 0.0, -3.0), 1e-12);
        Assert.AreEqual(0.0, AtmosphereLuts.RelativeAzimuth(0.0, 0.0, 1.0, 0.0), "a vertical ray has no azimuth");
    }
}
