using System.Numerics;
using RayTracer;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>
/// The M0 light model of integrator v0 (vista-plan §9: a C# reference and a parity test for shared shader
/// maths only). The shader's dome must be the engine's <see cref="Sky.DomeRadiance"/>, and the sun disk must
/// agree with the irradiance next-event estimation uses.
/// </summary>
[TestClass]
public sealed class SkyLightTests
{
    private static WavelengthLookup Lookup => IntegratorTables.Wavelengths;

    public static IEnumerable<object[]> Times => Enum.GetValues<TimeOfDay>().Select(t => new object[] { t });

    [TestMethod]
    [DynamicData(nameof(Times))]
    public void DomeRadiance_MatchesTheEngineSky(TimeOfDay time)
    {
        SunState sun = SunPlaceholder.For(time);
        SkyLight light = SkyLight.For(sun);
        var engine = new Sky(sun.Direction, skyStrength: sun.SkyStrength, sunTempK: sun.TempK, spectral: Lookup);
        Vector3[] directions =
        [
            Vector3.UnitY, new(0.3f, 0.4f, 0.8f), new(-0.9f, 0.05f, 0.1f), new(0.2f, -0.5f, 0.6f), -sun.Direction + new Vector3(0f, 0.2f, 0f),
        ];

        for (int i = 0; i < Lookup.DeterministicCount; i++)
        {
            float nm = Lookup.GetDeterministicWavelength(i);
            float shape = light.SolarShape(nm, Lookup);
            foreach (Vector3 dir in directions)
            {
                float expected = engine.DomeRadiance(dir, nm);
                float actual = light.DomeRadiance(dir, shape, SkyLight.RayleighRelative(nm));
                Assert.AreEqual(expected, actual, 1e-5f * Math.Max(1f, expected), $"{time}, {nm} nm, {dir}");
            }
        }
    }

    [TestMethod]
    public void For_FollowsTheSunState()
    {
        SunState day = SunPlaceholder.For(TimeOfDay.Day);
        SkyLight light = SkyLight.For(day);
        Assert.AreEqual(1f, light.ToSun.Length(), 1e-6f);
        Assert.AreEqual(-Vector3.Normalize(day.Direction), light.ToSun);
        Assert.AreEqual(day.SkyStrength, light.SkyStrength);
        Assert.AreEqual(day.TempK, light.SunTempK);
        Assert.AreEqual(SkyLight.PlaceholderSunIrradiance * day.SunLightLevel, light.SunIrradiance, 1e-6f);
        Assert.IsTrue(light.SunUp);
    }

    [TestMethod]
    public void Night_HasNoSun()
    {
        SkyLight night = SkyLight.For(SunPlaceholder.For(TimeOfDay.Night));
        Assert.IsFalse(night.SunUp);
        Assert.AreEqual(0f, night.SunIrradiance);
        Assert.IsTrue(night.SkyStrength > 0f, "the starlit floor of the dome stays");
    }

    [TestMethod]
    public void SunDisk_RadiatesTheIrradianceNextEventEstimationUses()
    {
        SkyLight light = SkyLight.For(SunPlaceholder.For(TimeOfDay.GoldenHour));
        float shape = light.SolarShape(550f, Lookup);
        Assert.AreEqual(light.SunSpectralIrradiance(shape), light.SunDiskRadiance(shape) * light.SunSolidAngle, 1e-3f);
        Assert.AreEqual(Math.PI * SkyLight.SunAngularRadiusRadians * SkyLight.SunAngularRadiusRadians, light.SunSolidAngle, 1e-9);
    }

    [TestMethod]
    public void DaySun_IsAboutFiveTimesTheDomeOnLevelGround()
    {
        SkyLight light = SkyLight.For(SunPlaceholder.For(TimeOfDay.Day));
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
