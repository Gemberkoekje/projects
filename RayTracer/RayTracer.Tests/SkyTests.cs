using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase O3 of the outdoor-area plan: the single-scattering Rayleigh <see cref="Sky"/> seen through an
/// open roof. Pins the sun disk (aligned with the directional light), the wavelength dependence that
/// makes the zenith blue, the horizon paling that is aerial perspective, the Rayleigh phase that tracks
/// the sun, and the solar colour-temperature knob that §O8 will lower to redden dusk.
/// </summary>
[TestClass]
public sealed class SkyTests
{
    // A sun near the eastern horizon, so the zenith (0,1,0) is dome (not the sun disk) for most tests.
    private static Sky Angled() => new(new Vector3(-1f, -0.2f, 0f));

    private static readonly Vector3 Zenith = new(0f, 1f, 0f);

    [TestMethod]
    public void SunDisk_IsToward_MinusDirection_AndBrightest()
    {
        var sky = new Sky(new Vector3(0f, -1f, 0f)); // sun straight up
        float sun = sky.Radiance(new Vector3(0f, 1f, 0f), 550f);    // straight up = the sun
        float dome = sky.Radiance(new Vector3(1f, 0.2f, 0f), 550f); // off to the side = dome
        Assert.AreEqual(sky.SunRadiance, sun, 1e-4f, "Looking toward −Direction hits the sun disk.");
        Assert.IsTrue(sun > dome * 2f, $"The sun disk is far brighter than the dome (sun {sun}, dome {dome}).");
    }

    [TestMethod]
    public void Zenith_IsBlueDominant()
    {
        var sky = Angled();
        float blue = sky.Radiance(Zenith, 450f);
        float red = sky.Radiance(Zenith, 650f);
        Assert.IsTrue(blue > red * 1.5f,
            $"Short wavelengths scatter far more at the zenith → a blue sky (450nm {blue} vs 650nm {red}).");
    }

    [TestMethod]
    public void Horizon_PalesTowardWhite()
    {
        // Aerial perspective: the long horizon airmass saturates every wavelength, so even weakly-scattered
        // red is much stronger at the horizon than at the zenith — the dome whitens toward the horizon.
        var sky = Angled();
        var horizon = Vector3.Normalize(new Vector3(0f, 0.02f, 1f)); // low, away from the sun's azimuth
        float redHorizon = sky.Radiance(horizon, 650f);
        float redZenith = sky.Radiance(Zenith, 650f);
        Assert.IsTrue(redHorizon > redZenith * 2f,
            $"Red scatters much more over the long horizon path (horizon {redHorizon}, zenith {redZenith}).");
    }

    [TestMethod]
    public void BrighterTowardSun_ThanPerpendicular()
    {
        // Rayleigh phase 0.75(1+cos²γ): at a fixed elevation, looking toward the sun's azimuth is brighter
        // than looking 90° away from it.
        var sky = Angled();
        float towardSun = sky.Radiance(Vector3.Normalize(new Vector3(1f, 0.1f, 0f)), 550f);
        float perpendicular = sky.Radiance(Vector3.Normalize(new Vector3(0f, 0.1f, 1f)), 550f);
        Assert.IsTrue(towardSun > perpendicular,
            $"The Rayleigh phase brightens the sky toward the sun (toward {towardSun}, perpendicular {perpendicular}).");
    }

    [TestMethod]
    public void CoolerSun_ReddensTheDome()
    {
        // §O8 hook: lowering SunTempK shifts the solar spectrum warm, raising the dome's red/blue ratio
        // even though Rayleigh keeps blue dominant.
        var warmDaylight = new Sky(new Vector3(-1f, -0.2f, 0f), sunTempK: 5778f);
        var dusk = new Sky(new Vector3(-1f, -0.2f, 0f), sunTempK: 3000f);
        float RedBlue(Sky s) => s.Radiance(Zenith, 650f) / s.Radiance(Zenith, 450f);
        Assert.IsTrue(RedBlue(dusk) > RedBlue(warmDaylight),
            "A cooler sun raises the dome's red/blue ratio (warmer sky).");
    }
}
