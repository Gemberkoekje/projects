using Vista.Atmosphere;

namespace Vista.Tests;

/// <summary>The 4D scattering-order tables' maps and integrands (vista-plan §10 M1.3; Bruneton 2017).</summary>
[TestClass]
public sealed class AtmosphereScatteringTests
{
    private static AtmosphereFrame Frame => AtmosphereLutsTests.Frame;

    [TestMethod]
    public void Texels_MapBackOntoThemselves()
    {
        foreach ((int ih, int imu, int imus, int inu) in new[] { (0, 0, 0, 0), (5, 10, 20, 3), (15, 31, 63, 7), (3, 32, 40, 4), (10, 63, 1, 6), (7, 45, 33, 2) })
        {
            AtmosphereScattering.Sample s = AtmosphereScattering.TexelToSample(Frame, ih, imu, imus, inu);
            AtmosphereScattering.Cell c = AtmosphereScattering.CellOf(Frame, s);
            Assert.AreEqual(ih, c.H0 + (int)Math.Round(c.Fh), $"height of ({ih}, {imu}, {imus}, {inu})");
            Assert.AreEqual(imu, c.Mu0 + (int)Math.Round(c.Fmu), $"view of ({ih}, {imu}, {imus}, {inu})");
            Assert.AreEqual(imus, c.MuS0 + (int)Math.Round(c.Fmus), $"sun of ({ih}, {imu}, {imus}, {inu})");
            Assert.AreEqual(s.Ground, imu >= AtmosphereScattering.MuHalfSize);
        }
    }

    [TestMethod]
    public void SunMap_IsDenseAtTheHorizon_AndRoundTrips()
    {
        Assert.AreEqual(AtmosphereScattering.MuSMin, AtmosphereScattering.MuSFromUnit(Frame, 0.0), 1e-9);
        Assert.AreEqual(1.0, AtmosphereScattering.MuSFromUnit(Frame, 1.0), 1e-9);
        foreach (double x in new[] { 0.1, 0.3, 0.5, 0.8 })
            Assert.AreEqual(x, AtmosphereScattering.MuSToUnit(Frame, AtmosphereScattering.MuSFromUnit(Frame, x)), 1e-9);
        double nearHorizon = AtmosphereScattering.MuSToUnit(Frame, 0.05) - AtmosphereScattering.MuSToUnit(Frame, -0.05);
        double nearZenith = AtmosphereScattering.MuSToUnit(Frame, 1.0) - AtmosphereScattering.MuSToUnit(Frame, 0.9);
        Assert.IsTrue(nearHorizon > 2.0 * nearZenith, $"{nearHorizon} against {nearZenith}");
    }

    [TestMethod]
    public void ClampNu_KeepsTheGeometryPossible()
    {
        Assert.AreEqual(0.5, AtmosphereScattering.ClampNu(1.0, 0.5, -1.0), 1e-12, "looking up, ν is the sun's cosine");
        Assert.AreEqual(0.3, AtmosphereScattering.ClampNu(0.0, 0.0, 0.3), 1e-12, "horizontal view and sun: anything goes");
    }

    [TestMethod]
    public void TableWavelengths_AreHeroesAndInterpolateInOrder()
    {
        IReadOnlyList<float> positions = AtmosphereScattering.MsPositions;
        Assert.AreEqual(50, positions.Count);
        for (int l = 0; l < AtmosphereScattering.MsWavelengths; l++)
            Assert.AreEqual(l, positions[AtmosphereScattering.HeroOf(l)], 1e-6, $"table wavelength {l}");
        for (int i = 1; i < positions.Count; i++)
            Assert.IsTrue(positions[i] >= positions[i - 1], "the hero cycle is in ascending wavelength");
        Assert.AreEqual(49, AtmosphereScattering.HeroOf(AtmosphereScattering.MsWavelengths - 1));
    }

    [TestMethod]
    public void DirectIrradiance_IsTheSunThroughTheAir()
    {
        AtmosphereCoefficients k = AtmosphereModel.Default.CoefficientsAt(550.0);
        int zenith = AtmosphereLuts.GroundIrradianceSize - 1;
        double exact = Math.Exp(-k.OpticalDepth(AtmosphereModel.Default.ZenithColumns(Frame.GroundAltitudeMetres)));
        Assert.AreEqual(exact, AtmosphereScattering.DirectIrradianceEntry(AtmosphereLutsTests.Columns, Frame, k, zenith), exact * 1e-3);
        Assert.AreEqual(0.0, AtmosphereScattering.DirectIrradianceEntry(AtmosphereLutsTests.Columns, Frame, k, 10), "the sun is down");
    }

    [TestMethod]
    public void IndirectIrradiance_OfAUniformSky_IsPi()
    {
        var uniform = new float[AtmosphereScattering.FloatsPerChannel];
        Array.Fill(uniform, 1f);
        double irradiance = AtmosphereScattering.IndirectIrradianceEntry([], uniform, firstOrder: false, Frame, 0, 100);
        Assert.AreEqual(Math.PI, irradiance, Math.PI * 3e-3, "∫ cos θ dω over the hemisphere, to the 16 × 16 quadrature");
    }

    [TestMethod]
    public void Density_OfAUniformField_IsEachPhaseIntegral()
    {
        // A uniform incident radiance of 1 everywhere (and an unlit ground): the phase-weighted radiance is each
        // phase function's integral over the sphere — 1 — up to the quadrature, which must resolve the haze's lobe.
        var uniform = new float[AtmosphereScattering.FloatsPerChannel];
        Array.Fill(uniform, 1f);
        var dark = new float[AtmosphereScattering.IrradianceFloats];
        AtmosphereCoefficients k = AtmosphereModel.Default.CoefficientsAt(550.0);
        foreach ((int ih, int imu, int imus, int inu) in new[] { (4, 10, 30, 4), (8, 31, 50, 1), (2, 40, 20, 6) })
        {
            (double rayleigh, double mie) = AtmosphereScattering.DensityTexel(AtmosphereLutsTests.Columns, [], uniform, dark, Frame, k, 3, 0, ih, imu, imus, inu);
            Assert.AreEqual(1.0, rayleigh, 0.01, $"Rayleigh at ({ih}, {imu}, {imus}, {inu})");
            Assert.AreEqual(1.0, mie, 0.03, $"haze at ({ih}, {imu}, {imus}, {inu})");
        }
    }

    [TestMethod]
    public void MultipleScattering_OfAConstantDensityTable_IsTheGatheredScattering()
    {
        // With A_R = A_M = 1 the density is the local scattering coefficient, so the gathered radiance along a ray is
        // ∫ T σ_s dt: positive, and below the ray's total scattering optical depth.
        var ones = new float[AtmosphereScattering.FloatsPerChannel * 2];
        Array.Fill(ones, 1f);
        AtmosphereCoefficients k = AtmosphereModel.Default.CoefficientsAt(450.0);
        double up = AtmosphereScattering.MultipleScatteringTexel(ones, Frame, k, 0, 0, 0, 40, 4);
        double level = AtmosphereScattering.MultipleScatteringTexel(ones, Frame, k, 0, 0, AtmosphereScattering.MuHalfSize - 1, 40, 4);
        Assert.IsTrue(up > 0.0 && up < 0.5, $"zenith {up}");
        Assert.IsTrue(level > up, $"the horizon gathers more ({level} against {up})");
    }

    [TestMethod]
    public void Density_BelowTheLowestSun_IsZero()
    {
        var ones = new float[AtmosphereScattering.FloatsPerChannel * 2];
        Array.Fill(ones, 1f);
        AtmosphereCoefficients k = AtmosphereModel.Default.CoefficientsAt(450.0);
        var twilight = new AtmosphereScattering.Sample(100.0, 0.2, AtmosphereScattering.MuSMin + 0.01, 0.0, false);
        Assert.IsTrue(AtmosphereScattering.Density(ones, Frame, k, 0.0, twilight) > 0.0);
        Assert.AreEqual(0.0, AtmosphereScattering.Density(ones, Frame, k, 0.0, twilight with { MuS = -0.8 }), "night");
    }

    [TestMethod]
    public void SingleScatteringTexel_IsZeroWithTheSunDeepBelow()
    {
        AtmosphereCoefficients k = AtmosphereModel.Default.CoefficientsAt(550.0);
        (double rayleigh, double mie) = AtmosphereScattering.SingleScatteringTexel(AtmosphereLutsTests.Columns, Frame, k, 0, 10, 0, 4);
        Assert.AreEqual(0.0, rayleigh, 1e-12, "μ_s = −0.5 from the ground: the whole ray is in the Earth's shadow");
        Assert.AreEqual(0.0, mie, 1e-12);
        (double lit, _) = AtmosphereScattering.SingleScatteringTexel(AtmosphereLutsTests.Columns, Frame, k, 0, 10, AtmosphereScattering.MuSSize - 1, 4);
        Assert.IsTrue(lit > 0.0);
    }
}
