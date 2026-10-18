using Vista.Recipes;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>The M0 sun placeholder: each time-of-day block lands where it should on the engine's day/night circle.</summary>
[TestClass]
public sealed class SunPlaceholderTests
{
    [TestMethod]
    public void DawnIsLowInTheEast_DuskLowInTheWest()
    {
        RayTracer.SunState dawn = SunPlaceholder.For(TimeOfDay.Dawn);
        RayTracer.SunState dusk = SunPlaceholder.For(TimeOfDay.Dusk);
        Assert.IsTrue(-dawn.Direction.X > 0.9f, "dawn sun is toward +X (east)");
        Assert.IsTrue(-dusk.Direction.X < -0.9f, "dusk sun is toward -X (west)");
        double dawnElevation = SunPlaceholder.ElevationDegrees(dawn);
        Assert.IsTrue(dawnElevation > 0.0 && dawnElevation < 6.0, $"dawn elevation {dawnElevation}");
        Assert.AreEqual(dawnElevation, SunPlaceholder.ElevationDegrees(dusk), 0.5);
    }

    [TestMethod]
    public void ElevationsOrderThroughTheDay()
    {
        double day = SunPlaceholder.ElevationDegrees(SunPlaceholder.For(TimeOfDay.Day));
        double golden = SunPlaceholder.ElevationDegrees(SunPlaceholder.For(TimeOfDay.GoldenHour));
        double dusk = SunPlaceholder.ElevationDegrees(SunPlaceholder.For(TimeOfDay.Dusk));
        double night = SunPlaceholder.ElevationDegrees(SunPlaceholder.For(TimeOfDay.Night));
        Assert.IsTrue(day > 60.0 && golden > dusk && dusk > 0.0 && night < -30.0, $"{day} {golden} {dusk} {night}");
    }

    [TestMethod]
    public void LowSunIsWarmer()
    {
        Assert.IsTrue(SunPlaceholder.For(TimeOfDay.Dawn).TempK < SunPlaceholder.For(TimeOfDay.Day).TempK);
        Assert.AreEqual(0f, SunPlaceholder.For(TimeOfDay.Night).SunRadiance);
    }
}
