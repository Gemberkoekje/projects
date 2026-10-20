using Vista.Atmosphere;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>The optics of clear air against published values (vista-plan §10 M1.3 verify).</summary>
[TestClass]
public sealed class AirOpticsTests
{
    [TestMethod]
    public void RayleighZenithDepth_At550nm_IsTheTabulatedValue()
    {
        // Bodhaine et al. 1999 (Table 3) and Bucholtz 1995 give 0.0973 for a sea-level standard atmosphere.
        Assert.AreEqual(0.0973, AirOptics.RayleighColumnOpticalDepth(550.0), 0.0973 * 0.01);
        (double rayleigh, _, _) = AtmosphereModel.Default.ZenithOpticalDepths(550.0, 0.0);
        Assert.AreEqual(AirOptics.RayleighColumnOpticalDepth(550.0), rayleigh, rayleigh * 1e-5, "the model's column is the standard atmosphere's (to its 100 km top)");
    }

    [TestMethod]
    public void RefractiveIndexAndKingFactor_MatchTheirSources()
    {
        // Peck & Reeder 1972 at 550 nm: (n − 1)·10⁸ = 27 782; Bates 1984 via Bodhaine: F_K(550) ≈ 1.0488.
        Assert.AreEqual(2.7782e-4, AirOptics.RefractivityMinusOne(550.0), 2e-8);
        Assert.AreEqual(1.0488, AirOptics.KingFactor(550.0), 5e-4);
    }

    [TestMethod]
    public void RayleighScattering_FallsSlightlyFasterThanTheFourthPower()
    {
        // The refractive index's dispersion steepens λ⁻⁴ to about λ⁻⁴·⁰⁸ in the visible.
        double ratio = AirOptics.RayleighCrossSection(450.0) / AirOptics.RayleighCrossSection(650.0);
        double exponent = Math.Log(ratio) / Math.Log(650.0 / 450.0);
        Assert.IsTrue(exponent is > 4.02 and < 4.15, $"exponent {exponent}");
    }

    [TestMethod]
    public void RayleighScaleHeight_HoldsTheStandardColumn()
    {
        Assert.AreEqual(8434.5, AirOptics.RayleighScaleHeightMetres, 1.0, "RT/(Mg) at 15 °C");
        Assert.AreEqual(2.547e25, AirOptics.StandardNumberDensity, 0.001e25);
    }

    [TestMethod]
    public void Ozone_PeaksInTheChappuisBandNear600nm()
    {
        IReadOnlyList<float> grid = AtmosphereTables.Build(AtmosphereModel.Default).WavelengthsNm;
        float peak = grid.MaxBy(nm => AirOptics.OzoneCrossSection(nm));
        Assert.IsTrue(peak is >= 590f and <= 610f, $"peak at {peak} nm");
        // 300 DU: about 0.04 at the Chappuis peak (5.0·10⁻²¹ cm² times 8.06·10¹⁸ molecules/cm²).
        (_, _, double ozone) = AtmosphereModel.Default.ZenithOpticalDepths(602.0, 0.0);
        Assert.AreEqual(0.039, ozone, 0.002);
    }

    [TestMethod]
    public void OzoneTable_IsReadAtItsBinCentres()
    {
        Assert.AreEqual(AirOptics.OzoneTable[24], AirOptics.OzoneCrossSection(605.0), 1e-40, "the 600–610 nm bin sits at 605 nm");
        double between = AirOptics.OzoneCrossSection(600.0);
        Assert.AreEqual(0.5 * (AirOptics.OzoneTable[23] + AirOptics.OzoneTable[24]), between, 1e-40);
        Assert.AreEqual(AirOptics.OzoneTable[0], AirOptics.OzoneCrossSection(300.0), 1e-40, "constant beyond the table");
    }

    [TestMethod]
    public void PhaseFunctions_IntegrateToOne()
    {
        foreach (double g in new[] { 0.0, 0.5, 0.8 })
            Assert.AreEqual(1.0, SphereIntegral(c => AirOptics.HenyeyGreenstein(c, g)), 1e-4, $"Henyey–Greenstein g = {g}");
        Assert.AreEqual(1.0, SphereIntegral(AirOptics.RayleighPhase), 1e-6, "Rayleigh");
        Assert.IsTrue(AirOptics.HenyeyGreenstein(1.0, 0.8) > 10.0 * AirOptics.HenyeyGreenstein(0.0, 0.8), "forward lobe");
    }

    private static double SphereIntegral(Func<double, double> phase)
    {
        const int n = 20000;
        double sum = 0.0;
        for (int i = 0; i < n; i++)
        {
            double mu = -1.0 + 2.0 * (i + 0.5) / n;
            sum += phase(mu);
        }
        return sum * 2.0 / n * 2.0 * Math.PI;
    }

    [TestMethod]
    public void Sun_IsTheSolarConstantOfA5778KBlackbody()
    {
        double total = 0.0;
        for (double nm = 100.5; nm < 20000.0; nm += 1.0)
            total += SolarSpectrum.Irradiance(nm);
        Assert.AreEqual(1368.0, total, 3.0, "σT⁴ through the sun's solid angle");
        Assert.AreEqual(1.76, SolarSpectrum.Irradiance(550.0), 0.01);
        Assert.AreEqual(Math.PI * SolarSpectrum.AngularRadiusRadians * SolarSpectrum.AngularRadiusRadians, SolarSpectrum.SolidAngle, 1e-9);
    }

    [TestMethod]
    public void SunDisk_OneMinusCos_IsExactInDouble()
    {
        double exact = 1.0 - Math.Cos(SolarSpectrum.AngularRadiusRadians);
        Assert.AreEqual(exact, SolarSpectrum.OneMinusCosRadius, exact * 1e-9);
        float naive = 1f - MathF.Cos((float)SolarSpectrum.AngularRadiusRadians);
        Assert.IsTrue(Math.Abs(naive - exact) > exact * 1e-3, "float subtraction is why the shader takes the double value");
    }

    [TestMethod]
    public void LuminanceOfTheSun_IsAboutAThousandMegacandela()
    {
        // The ȳ-weighted mean spectral radiance of the disk times 683 · ∫ȳ: the sun's luminance is ≈ 1.6·10⁹ cd/m².
        double sum = 0.0, weight = 0.0;
        IntegratorTables tables = IntegratorTables.Build(Scene.MaterialCatalog.M0);
        for (int i = 0; i < tables.Count; i++)
        {
            sum += tables.Cie[i * 4 + 1] * SolarSpectrum.Radiance(tables.WavelengthNm(i));
            weight += tables.Cie[i * 4 + 1];
        }
        double luminance = sum / weight * IntegratorTables.LuminancePerY;
        Assert.IsTrue(luminance is > 1.3e9 and < 2.2e9, $"{luminance:G3} cd/m²");
    }
}
