using System.Numerics;
using RayTracer;
using Vista.Astronomy;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// The M0 light model of integrator v0 (vista-plan §9: a C# reference and a parity test for shared shader
/// maths only). The shader's dome must be the engine's <see cref="Sky.DomeRadiance"/>, the light's level and
/// colour must follow the sun's height as the engine's <see cref="DayNightCycle"/> did, and the sun disk must
/// agree with the irradiance next-event estimation uses.
/// </summary>
[TestClass]
public sealed class SkyLightTests
{
    private static WavelengthLookup Lookup => IntegratorTables.Wavelengths;

    /// <summary>The M0 day/night cycle's noon sun: high, tilted a little north.</summary>
    private static readonly Vector3 HighSun = Vector3.Normalize(new Vector3(0f, 1f, 0.3f));

    private static SkyLight AtElevation(double azimuth, double elevation) =>
        SkyLight.For(new SunPosition(azimuth, elevation, elevation, 0.0, 0.0));

    [TestMethod]
    [DataRow(-10.0)]
    [DataRow(-2.0)]
    [DataRow(3.0)]
    [DataRow(9.0)]
    [DataRow(30.0)]
    [DataRow(70.0)]
    public void DomeRadiance_MatchesTheEngineSky(double elevation)
    {
        SkyLight light = AtElevation(250.0, elevation);
        var engine = new Sky(-light.ToSun, skyStrength: light.SkyStrength, sunTempK: light.SunTempK, spectral: Lookup);
        Vector3[] directions =
        [
            Vector3.UnitY, new(0.3f, 0.4f, 0.8f), new(-0.9f, 0.05f, 0.1f), new(0.2f, -0.5f, 0.6f), light.ToSun + new Vector3(0f, 0.2f, 0f),
        ];

        for (int i = 0; i < Lookup.DeterministicCount; i++)
        {
            float nm = Lookup.GetDeterministicWavelength(i);
            float shape = light.SolarShape(nm, Lookup);
            foreach (Vector3 dir in directions)
            {
                float expected = engine.DomeRadiance(dir, nm);
                float actual = light.DomeRadiance(dir, shape, SkyLight.RayleighRelative(nm));
                Assert.AreEqual(expected, actual, 1e-5f * Math.Max(1f, expected), $"{elevation}°, {nm} nm, {dir}");
            }
        }
    }

    [TestMethod]
    public void LightLevelAndColour_FollowTheSunsHeightLikeTheEnginesDayNightCycle()
    {
        // Vista no longer uses the cycle's circle, only its curves of height; this keeps them identical, so M0
        // and M1.2 light a sun at the same height the same way.
        for (int i = 0; i < 200; i++)
        {
            SunState cycle = DayNightCycle.At(i / 200f);
            SkyLight light = SkyLight.ForSunDirection(-cycle.Direction);
            Assert.AreEqual(1f, light.ToSun.Length(), 1e-6f);
            Assert.AreEqual(cycle.SkyStrength, light.SkyStrength, 1e-6f, $"phase {i / 200f}");
            Assert.AreEqual(cycle.TempK, light.SunTempK, 1e-3f, $"phase {i / 200f}");
            float irradiance = -cycle.Direction.Y > 0f ? SkyLight.PlaceholderSunIrradiance * cycle.SunLightLevel : 0f;
            Assert.AreEqual(irradiance, light.SunIrradiance, 1e-6f, $"phase {i / 200f}");
        }
    }

    [TestMethod]
    public void For_PutsTheSunWhereTheAstronomySays()
    {
        var sun = new SunPosition(250.0, 9.0, 8.9, 0.0, 0.0);
        SkyLight light = SkyLight.For(sun);
        Assert.AreEqual(sun.ToSun, light.ToSun);
        Assert.IsTrue(light.SunUp);
        Assert.IsTrue(light.SunTempK < 4000f, "a low sun is warm");
    }

    [TestMethod]
    public void Night_HasNoSun()
    {
        SkyLight night = AtElevation(0.0, -30.0);
        Assert.IsFalse(night.SunUp);
        Assert.AreEqual(0f, night.SunIrradiance);
        Assert.IsTrue(night.SkyStrength > 0f, "the starlit floor of the dome stays");
    }

    [TestMethod]
    public void SunDisk_RadiatesTheIrradianceNextEventEstimationUses()
    {
        SkyLight light = AtElevation(260.0, 9.0);
        float shape = light.SolarShape(550f, Lookup);
        Assert.AreEqual(light.SunSpectralIrradiance(shape), light.SunDiskRadiance(shape) * light.SunSolidAngle, 1e-3f);
        Assert.AreEqual(Math.PI * SkyLight.SunAngularRadiusRadians * SkyLight.SunAngularRadiusRadians, light.SunSolidAngle, 1e-9);
    }

    [TestMethod]
    public void DaySun_IsAboutFiveTimesTheDomeOnLevelGround()
    {
        SkyLight light = SkyLight.ForSunDirection(HighSun);
        float shape = light.SolarShape(550f, Lookup);
        float rayleigh = SkyLight.RayleighRelative(550f);

        // Irradiance of the dome on an upward-facing surface: ∫ L cos θ dω over the upper hemisphere.
        const int rings = 180, segments = 360;
        double dome = 0.0;
        for (int i = 0; i < rings; i++)
        {
            double theta = (i + 0.5) / rings * Math.PI / 2.0;
            for (int j = 0; j < segments; j++)
            {
                double phi = (j + 0.5) / segments * 2.0 * Math.PI;
                var dir = new Vector3((float)(Math.Sin(theta) * Math.Cos(phi)), (float)Math.Cos(theta), (float)(Math.Sin(theta) * Math.Sin(phi)));
                dome += light.DomeRadiance(dir, shape, rayleigh) * Math.Cos(theta) * Math.Sin(theta);
            }
        }
        dome *= Math.PI / 2.0 / rings * (2.0 * Math.PI / segments);
        double sun = light.SunSpectralIrradiance(shape) * light.ToSun.Y;

        double ratio = sun / dome;
        Assert.IsTrue(ratio is > 4.0 and < 6.0, $"sun / dome = {ratio:0.00} (dome {dome:0.000}, sun {sun:0.000})");
    }

    [TestMethod]
    public void RayleighRelative_IsOneAt550AndRisesTowardTheBlue()
    {
        Assert.AreEqual(1f, SkyLight.RayleighRelative(550f), 1e-6f);
        Assert.AreEqual(MathF.Pow(550f / 450f, 4f), SkyLight.RayleighRelative(450f), 1e-5f);
    }
}
