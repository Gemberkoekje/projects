using Vista.Atmosphere;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>
/// The C# reference of aerial perspective (vista-plan §10 M1.4): the world bent onto the Earth for the air, the march
/// along a segment against brute-force integrals, the sun's penumbra, and the quadrature and table maps M1.4 changed.
/// The GPU's parity with these runs in <c>--gpu-selftest</c>.
/// </summary>
[TestClass]
public sealed class AerialPerspectiveTests
{
    private static AtmosphereFrame Frame => AtmosphereLutsTests.Frame;

    private static float[] Columns => AtmosphereLutsTests.Columns;

    [TestMethod]
    public void CurvatureDrop_IsAbout200MetresAt50Kilometres()
    {
        double drop = AerialPerspective.CurvatureDrop(Frame.GroundRadius, 50_000.0);
        Assert.AreEqual(50_000.0 * 50_000.0 / (2.0 * Frame.GroundRadius), drop, 0.01, "s²/2R");
        Assert.AreEqual(196.5, drop, 0.5);
        Assert.AreEqual(0.0, AerialPerspective.CurvatureDrop(Frame.GroundRadius, 0.0));
    }

    [TestMethod]
    public void Bend_KeepsHeights_AndTiltsTheVertical()
    {
        double radius = Frame.GroundRadius;
        (Vector3D atCamera, Vector3D upAtCamera) = AerialPerspective.Bend(radius, 100.0, -200.0, new Vector3D(100.0, 1700.0, -200.0));
        Assert.AreEqual(new Vector3D(0.0, 1700.0, 0.0), atCamera, "the camera stands on its own tangent point");
        Assert.AreEqual(new Vector3D(0.0, 1.0, 0.0), upAtCamera);

        (Vector3D far, Vector3D up) = AerialPerspective.Bend(radius, 0.0, 0.0, new Vector3D(30_000.0, 500.0, 40_000.0));
        Vector3D centre = new(0.0, -radius, 0.0);
        Assert.AreEqual(radius + 500.0, (far - centre).Length(), 1e-6, "the point keeps its height above the ground sphere");
        Assert.AreEqual(50_000.0 / radius, Math.Acos(up.Y), 1e-12, "its vertical tilts by s/R");
        Assert.AreEqual(1.0, up.Length(), 1e-12);
        Assert.AreEqual(0.6, up.X / Math.Sqrt(up.X * up.X + up.Z * up.Z), 1e-12, "toward the point");
        Assert.AreEqual(500.0 * Math.Cos(50_000.0 / radius) - AerialPerspective.CurvatureDrop(radius, 50_000.0), far.Y, 1e-6, "it sinks by the drop");
    }

    [TestMethod]
    public void Between_EndsAtExactlyTheFarPointsHeight()
    {
        foreach ((Vector3D a, Vector3D b) in new[]
        {
            (new Vector3D(0.0, 1700.0, -5000.0), new Vector3D(3000.0, 600.0, 15_000.0)),
            (new Vector3D(-2000.0, 800.0, 1000.0), new Vector3D(-2100.0, 950.0, 1300.0)),
            (new Vector3D(4000.0, 2400.0, 4000.0), new Vector3D(-30_000.0, 10.0, -35_000.0)),
        })
        {
            AerialPerspective.Segment seg = AerialPerspective.Between(Frame, 0.0, -5000.0, a, b);
            Assert.AreEqual(a.Y, seg.Height, 1e-9);
            double end = AtmosphereLuts.AltitudeAlong(Frame.GroundRadius, seg.Height, seg.Mu, seg.Length);
            Assert.AreEqual(b.Y, end, 1e-5, $"{a} → {b}");
        }
    }

    [TestMethod]
    public void Between_DipsTowardALevelPointBehindTheCurve()
    {
        // From the camera to a point at its own height 40 km away, the flat ray is level; the bent chord dips by s/2R.
        var camera = new Vector3D(0.0, 1500.0, 0.0);
        AerialPerspective.Segment seg = AerialPerspective.Between(Frame, 0.0, 0.0, camera, new Vector3D(40_000.0, 1500.0, 0.0));
        Assert.AreEqual(-40_000.0 / (2.0 * Frame.GroundRadius), Math.Asin(seg.Mu), 2e-6);
        Assert.AreEqual(Frame.SunCosZenith, seg.MuS, 1e-7, "the camera's own sun");
    }

    [TestMethod]
    public void Steps_GrowWithLength()
    {
        Assert.AreEqual(2, AerialPerspective.StepsFor(0.0));
        Assert.AreEqual(2, AerialPerspective.StepsFor(300.0));
        Assert.AreEqual(5, AerialPerspective.StepsFor(3500.0));
        Assert.AreEqual(AerialPerspective.MaxSteps, AerialPerspective.StepsFor(50_000.0));
    }

    [TestMethod]
    public void Integrate_AnEmptySegment_IsClear()
    {
        AtmosphereCoefficients k = Frame.Model.CoefficientsAt(450.0);
        (double t, double s) = AerialPerspective.Integrate(Columns, [], Frame, k, 0.0, new AerialPerspective.Segment(500.0, -0.1, 0.3, 0.2, 0.0));
        Assert.AreEqual(1.0, t);
        Assert.AreEqual(0.0, s);
    }

    [TestMethod]
    public void Transmittance_MatchesAFineIntegral()
    {
        foreach (double nm in new[] { 400.0, 550.0, 650.0 })
        {
            AtmosphereCoefficients k = Frame.Model.CoefficientsAt(nm);
            foreach (AerialPerspective.Segment seg in Segments())
            {
                double depth = 0.0;
                const int fine = 20_000;
                for (int i = 0; i < fine; i++)
                {
                    double t = (i + 0.5) * seg.Length / fine;
                    double h = Math.Max(AtmosphereLuts.AltitudeAlong(Frame.GroundRadius, seg.Height, seg.Mu, t), 0.0);
                    depth += AtmosphereLuts.Coefficients(Frame.Model, k, Frame.GroundAltitudeMetres + h).Extinction * seg.Length / fine;
                }
                (double marched, _) = AerialPerspective.Integrate(Columns, [], Frame, k, 0.0, seg);
                Assert.AreEqual(Math.Exp(-depth), marched, 2e-3 * Math.Exp(-depth), $"{nm} nm, {seg}");
            }
        }
    }

    [TestMethod]
    public void SingleScattering_MatchesABruteForceMarch()
    {
        foreach (double nm in new[] { 450.0, 600.0 })
        {
            AtmosphereCoefficients k = Frame.Model.CoefficientsAt(nm);
            foreach (AerialPerspective.Segment seg in Segments())
            {
                double phaseR = AirOptics.RayleighPhase(seg.Nu), phaseM = AirOptics.HenyeyGreenstein(seg.Nu, Frame.Model.AerosolAsymmetry);
                double rb = Frame.GroundRadius, r0 = rb + seg.Height;
                double transmittance = 1.0, brute = 0.0;
                const int fine = 4000;
                double dt = seg.Length / fine;
                for (int i = 0; i < fine; i++)
                {
                    double t = (i + 0.5) * dt;
                    double h = Math.Max(AtmosphereLuts.AltitudeAlong(rb, seg.Height, seg.Mu, t), 0.0);
                    double muS = (r0 * seg.MuS + t * seg.Nu) / (rb + h);
                    (_, double extinction, double sigmaR, double sigmaM) = AtmosphereLuts.Coefficients(Frame.Model, k, Frame.GroundAltitudeMetres + h);
                    double sun = AtmosphereLuts.SunLight(Columns, Frame, k, h, muS);
                    transmittance *= Math.Exp(-0.5 * extinction * dt);
                    brute += transmittance * sun * (sigmaR * phaseR + sigmaM * phaseM) * dt;
                    transmittance *= Math.Exp(-0.5 * extinction * dt);
                }
                (_, double marched) = AerialPerspective.Integrate(Columns, [], Frame, k, 0.0, seg);
                Assert.AreEqual(brute, marched, 5e-3 * brute, $"{nm} nm, {seg}");
            }
        }
    }

    /// <summary>Segments like the terrain's: down from a camera to the valley, across it, and up a slope; 0.2 to 20 km.</summary>
    private static IEnumerable<AerialPerspective.Segment> Segments()
    {
        double muS = Frame.SunCosZenith;
        yield return new AerialPerspective.Segment(1700.0, -0.08, muS, -0.1, 20_000.0);
        yield return new AerialPerspective.Segment(1700.0, -0.3, muS, 0.2, 5000.0);
        yield return new AerialPerspective.Segment(600.0, 0.01, muS, 0.6, 12_000.0);
        yield return new AerialPerspective.Segment(500.0, 0.25, muS, -0.4, 3000.0);
        yield return new AerialPerspective.Segment(900.0, -0.05, muS, 0.0, 200.0);
    }

    [TestMethod]
    public void SunVisibility_HasAPenumbra()
    {
        const double height = 3000.0;
        double dip = Math.Atan2(Math.Sqrt(height * (2.0 * Frame.GroundRadius + height)), Frame.GroundRadius);
        double radius = SolarSpectrum.AngularRadiusRadians;
        Assert.AreEqual(1.0, AtmosphereLuts.SunVisibility(Frame, height, 0.2).Fraction, "high sun");
        Assert.AreEqual(0.5, AtmosphereLuts.SunVisibility(Frame, height, -Math.Sin(dip)).Fraction, 1e-9, "centre on the horizon");
        Assert.AreEqual(0.0, AtmosphereLuts.SunVisibility(Frame, height, -Math.Sin(dip + 1.01 * radius)).Fraction, "set");
        Assert.AreEqual(1.0, AtmosphereLuts.SunVisibility(Frame, height, -Math.Sin(dip - 1.01 * radius)).Fraction, "just risen");
        (double quarter, double sunMu) = AtmosphereLuts.SunVisibility(Frame, height, -Math.Sin(dip + 0.4 * radius));
        Assert.IsTrue(quarter is > 0.1 and < 0.5, $"a lit sliver: {quarter}");
        Assert.IsTrue(sunMu > -Math.Sin(dip + 0.4 * radius), "its light grazes the horizon, not the hidden centre");
        Assert.IsFalse(AtmosphereLuts.HitsSphere(Frame.GroundRadius, height, sunMu));
    }

    [TestMethod]
    public void DensityQuadrature_CoversTheSphere_AndNeverStraddlesTheHorizon()
    {
        foreach (double height in new[] { 1.0, 3800.0, 40_000.0 })
        {
            double horizon = AtmosphereLuts.Horizon(Frame.GroundRadius, height).HorizonZenith;
            (double above, double below) = AtmosphereScattering.DensityScales(horizon);
            Assert.AreEqual(1.0, above, 4e-3, "the warped midpoint rule alone is within 0.4%");
            Assert.AreEqual(1.0, below, 4e-3);
            double solid = 0.0, lastSky = 0.0, firstGround = Math.PI;
            for (int i = 0; i < AtmosphereScattering.DensityTheta; i++)
            {
                (double theta, double dTheta, bool ground) = AtmosphereScattering.DensityPolar(i, horizon);
                solid += Math.Sin(theta) * dTheta * (ground ? below : above);
                if (ground)
                    firstGround = Math.Min(firstGround, theta);
                else
                    lastSky = Math.Max(lastSky, theta);
            }
            Assert.AreEqual(2.0, solid, 1e-12, $"∫ sin θ dθ at {height} m, corrected");
            Assert.IsTrue(lastSky < horizon && horizon < firstGround, $"at {height} m");
        }
    }

    [TestMethod]
    public void GroundIrradianceEntries_SitOnTheSunMap_DenseAtTheHorizon()
    {
        int n = AtmosphereLuts.GroundIrradianceSize;
        Assert.AreEqual(AtmosphereScattering.MuSMin, AtmosphereLuts.GroundIrradianceMuS(Frame, 0), 1e-9);
        Assert.AreEqual(1.0, AtmosphereLuts.GroundIrradianceMuS(Frame, n - 1), 1e-9);
        double nearHorizon = double.MaxValue;
        for (int i = 1; i < n; i++)
        {
            double a = AtmosphereLuts.GroundIrradianceMuS(Frame, i - 1), b = AtmosphereLuts.GroundIrradianceMuS(Frame, i);
            Assert.IsTrue(b > a, $"entry {i}");
            if (a <= 0.0 && b >= 0.0)
                nearHorizon = b - a;
        }
        Assert.IsTrue(nearHorizon < 0.006, $"under 0.35° apart at the horizon: {nearHorizon}");
    }

    [TestMethod]
    public void ViewMap_RoundTrips_AndCoversTheZenithNearTheGround()
    {
        foreach (int ih in new[] { 0, 3, 15 })
        {
            foreach (int imu in new[] { 0, 1, 16, 31, 32, 33, 50, 63 })
            {
                AtmosphereScattering.Sample s = AtmosphereScattering.TexelToSample(Frame, ih, imu, 10, 3);
                AtmosphereScattering.Cell c = AtmosphereScattering.CellOf(Frame, s);
                Assert.AreEqual(imu, c.Mu0 + (int)Math.Round(c.Fmu), $"view texel {imu} at height texel {ih}");
            }
        }
        // Near the ground the first texel off the zenith is a few degrees away, not 41° as on Bruneton's map by distance.
        double first = AtmosphereScattering.TexelToSample(Frame, 0, 1, 0, 0).Mu;
        Assert.IsTrue(first > Math.Cos(10.0 * Math.PI / 180.0), $"texel 1 at {Math.Acos(first) * 180.0 / Math.PI:0.0}° from the zenith");
        double nadirSide = AtmosphereScattering.TexelToSample(Frame, 0, AtmosphereScattering.MuSize - 2, 0, 0).Mu;
        Assert.IsTrue(nadirSide < -Math.Cos(10.0 * Math.PI / 180.0), "and the ground half reaches the nadir in steps of degrees");
    }
}
