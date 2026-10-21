using Vista.Astronomy;
using Vista.Recipes;

namespace Vista.Tests;

/// <summary>
/// A recipe resolved to a sun (vista-plan §3.5, §10 M1.2, M1.11): the place, the season's date or a pinned date, and the
/// time-of-day presets by sun height, falling back to the sun's nearest approach where it never reaches one.
/// </summary>
[TestClass]
public sealed class AlmanacTests
{
    [TestMethod]
    public void DefaultRecipe_IsZermattAtMidsummerNoon()
    {
        Almanac almanac = Almanac.For(new Recipe());
        Assert.AreEqual(Place.Zermatt, almanac.Place.Place);
        Assert.AreEqual(new DateOnly(SeasonCalendar.ReferenceYear, 7, 15), almanac.Date);
        Assert.IsTrue(almanac.DateFromSeason);
        Assert.AreEqual(Season.Summer, almanac.Season);
        Assert.AreEqual(12.0, almanac.SolarHours);
        Assert.AreEqual("day: solar noon", almanac.SolarTimeFrom);
        Assert.AreEqual(180.0, almanac.Sun.AzimuthDegrees, 1e-9, "the noon sun is due south at Zermatt");
        Assert.IsTrue(almanac.Sun.ElevationDegrees is > 60.0 and < 66.0, $"{almanac.Sun.ElevationDegrees}");
        Assert.AreEqual(0, almanac.Hints.Count);
    }

    [TestMethod]
    public void Presets_AreSunHeights()
    {
        var recipe = new Recipe { Location = Place.Zermatt, Season = Season.Autumn };
        Almanac dawn = At(recipe, TimeOfDay.Dawn);
        Almanac golden = At(recipe, TimeOfDay.GoldenHour);
        Almanac dusk = At(recipe, TimeOfDay.Dusk);
        Assert.AreEqual(Almanac.DawnElevationDegrees, dawn.Sun.TrueElevationDegrees, 1e-6);
        Assert.AreEqual(Almanac.GoldenHourElevationDegrees, golden.Sun.TrueElevationDegrees, 1e-6);
        Assert.AreEqual(Almanac.DuskElevationDegrees, dusk.Sun.TrueElevationDegrees, 1e-6);
        Assert.IsTrue(dawn.SolarHours < 12.0 && golden.SolarHours > 12.0 && dusk.SolarHours > golden.SolarHours, "dawn rises, golden hour and dusk set");
        Assert.AreEqual(12.0, At(recipe, TimeOfDay.Day).SolarHours);
        Assert.AreEqual(0.0, At(recipe, TimeOfDay.Night).SolarHours);
        Assert.AreEqual("dawn: sun at −4°, rising", dawn.SolarTimeFrom);
        Assert.AreEqual("goldenHour: sun at +2°, setting", golden.SolarTimeFrom);
        Assert.AreEqual("dusk: sun at −4°, setting", dusk.SolarTimeFrom);
        Assert.AreEqual(0, dawn.Hints.Count + golden.Hints.Count + dusk.Hints.Count);
    }

    [TestMethod]
    public void DawnAndDusk_AreTwilight_GoldenHourIsALowSun()
    {
        var recipe = new Recipe { Location = Place.Zermatt, Season = Season.Autumn };
        foreach (TimeOfDay time in new[] { TimeOfDay.Dawn, TimeOfDay.Dusk })
        {
            double elevation = At(recipe, time).Sun.ElevationDegrees;
            Assert.IsTrue(elevation is > -6.0 and < -2.0, $"{time} at {elevation:0.00}° should be civil twilight");
        }
        double golden = At(recipe, TimeOfDay.GoldenHour).Sun.ElevationDegrees;
        Assert.IsTrue(golden > Almanac.GoldenHourElevationDegrees && golden < Almanac.GoldenHourElevationDegrees + 0.6,
            $"golden hour at {golden:0.00}° (apparent: lifted by refraction)");
        Assert.IsTrue(At(recipe, TimeOfDay.Dawn).Sun.AzimuthDegrees is > 90.0 and < 120.0, "dawn in the east-southeast");
        Assert.IsTrue(At(recipe, TimeOfDay.Dusk).Sun.AzimuthDegrees is > 240.0 and < 270.0, "dusk in the west-southwest");
        Assert.IsTrue(At(recipe, TimeOfDay.Night).Sun.ElevationDegrees < -18.0, "solar midnight is astronomical night");
    }

    [TestMethod]
    public void EveryPreset_LandsAtItsHeight_AtEveryPlaceInEverySeason_OrFallsBackWithAHint()
    {
        int fallbacks = 0;
        foreach (PlaceInfo place in Places.All)
        {
            foreach (Season season in Enum.GetValues<Season>())
            {
                var recipe = new Recipe { Location = place.Place, Season = season };
                foreach (TimeOfDay time in new[] { TimeOfDay.Dawn, TimeOfDay.GoldenHour, TimeOfDay.Dusk })
                {
                    Almanac almanac = At(recipe, time);
                    double target = Almanac.PresetElevation(time);
                    string what = $"{time} at {place.Name} in {season} ({almanac.Date:yyyy-MM-dd})";
                    if (almanac.Hints.Count == 0)
                    {
                        Assert.AreEqual(target, almanac.Sun.TrueElevationDegrees, 1e-6, what);
                        Assert.AreEqual(time == TimeOfDay.Dawn, almanac.SolarHours < 12.0, $"{what}: dawn rises, the others set");
                        continue;
                    }
                    // The sun never reaches the height: the preset takes its nearest approach, and says so.
                    fallbacks++;
                    bool highest = almanac.SolarHours == 12.0;
                    Assert.IsTrue(highest || almanac.SolarHours is 0.0 or 24.0, $"{what}: noon or midnight, got {almanac.SolarHours}");
                    double noon = SolarPosition.At(place.LatitudeDegrees, almanac.Date, 12.0).TrueElevationDegrees;
                    double midnight = SolarPosition.At(place.LatitudeDegrees, almanac.Date, 0.0).TrueElevationDegrees;
                    Assert.IsTrue(highest ? noon < target : midnight > target, $"{what}: the height is out of the sun's reach");
                    StringAssert.Contains(almanac.Hints.Single(), highest ? "never climbs to" : "never sinks to", what);
                }
            }
        }
        // Only Lofoten is ever out of reach: its July white night never sinks to −4°, and its January sun never climbs to +2°.
        Assert.AreEqual(3, fallbacks, "dawn and dusk at Lofoten in July, golden hour there in January");
    }

    [TestMethod]
    public void AGoldenHourByHeight_IsTheSameLightEverywhere()
    {
        // M1.2's "sunset − 60 min" put the sun from 4.3° (Lofoten) to 10.7° (Yosemite) on the same October date.
        foreach (Place place in new[] { Place.Zermatt, Place.Lofoten, Place.Yosemite, Place.TorresDelPaine })
        {
            Almanac almanac = At(new Recipe { Location = place, Season = Season.Autumn }, TimeOfDay.GoldenHour);
            Assert.AreEqual(Almanac.GoldenHourElevationDegrees, almanac.Sun.TrueElevationDegrees, 1e-6, place.ToString());
        }
    }

    [TestMethod]
    public void AutumnAtTorresDelPaine_IsAnAprilSunInTheNorth()
    {
        Almanac almanac = Almanac.For(new Recipe { Location = Place.TorresDelPaine, Season = Season.Autumn });
        Assert.AreEqual(4, almanac.Date.Month);
        Assert.AreEqual(0.0, almanac.Sun.AzimuthDegrees, 1e-9, "the southern noon sun is due north");
        Assert.IsTrue(almanac.Place.Southern);
    }

    [TestMethod]
    public void Date_OverridesTheSeason()
    {
        Almanac almanac = Almanac.For(new Recipe { Season = Season.Winter, Date = new DateOnly(2026, 7, 1) });
        Assert.AreEqual(new DateOnly(2026, 7, 1), almanac.Date);
        Assert.IsFalse(almanac.DateFromSeason);
        Assert.AreEqual(Season.Summer, almanac.Season, "the season follows the date");
    }

    [TestMethod]
    public void SolarTime_OverridesThePreset()
    {
        Almanac almanac = Almanac.For(new Recipe { Time = TimeOfDay.Dawn, SolarTime = new TimeOnly(17, 30) });
        Assert.AreEqual(17.5, almanac.SolarHours);
        Assert.AreEqual("pinned solarTime", almanac.SolarTimeFrom);
        Assert.AreEqual(SolarPosition.At(46.02, almanac.Date, 17.5), almanac.Sun);
    }

    [TestMethod]
    public void CustomLatitude_ReplacesThePlace()
    {
        Almanac almanac = Almanac.For(new Recipe { Latitude = -33.9, Season = Season.Summer });
        Assert.IsTrue(almanac.Place.IsCustom);
        Assert.AreEqual(-33.9, almanac.Place.LatitudeDegrees);
        Assert.AreEqual(Places.CustomAltitudeMetres, almanac.Place.ValleyAltitudeMetres);
        Assert.AreEqual(1, almanac.Date.Month, "southern summer is January");
    }

    [TestMethod]
    public void PolarDay_SendsDawnAndDuskToMidnight_WithAHint()
    {
        var recipe = new Recipe { Location = Place.Lofoten, Date = new DateOnly(2026, 6, 21) };
        Assert.AreEqual(DaylightKind.PolarDay, At(recipe, TimeOfDay.Day).Daylight.Kind);
        foreach (TimeOfDay time in new[] { TimeOfDay.Dawn, TimeOfDay.Dusk })
        {
            Almanac almanac = At(recipe, time);
            Assert.AreEqual(time == TimeOfDay.Dawn ? 0.0 : 24.0, almanac.SolarHours, $"{time}");
            Assert.IsTrue(almanac.Sun.ElevationDegrees > 0.0, "the midnight sun is up");
            StringAssert.Contains(almanac.Hints.Single(), "never sinks to −4° at Lofoten on 2026-06-21 (polar day)");
        }
        Almanac golden = At(recipe, TimeOfDay.GoldenHour);
        Assert.AreEqual(0, golden.Hints.Count, "the midnight sun still sinks to +2° in the evening");
        Assert.AreEqual(Almanac.GoldenHourElevationDegrees, golden.Sun.TrueElevationDegrees, 1e-6);
        Assert.AreEqual(0, At(recipe, TimeOfDay.Day).Hints.Count);
        Assert.AreEqual(0, At(recipe, TimeOfDay.Night).Hints.Count, "night is solar midnight anyway");
    }

    [TestMethod]
    public void PolarNight_SendsGoldenHourToNoon_WithAHint_AndKeepsItsTwilight()
    {
        var recipe = new Recipe { Location = Place.Lofoten, Date = new DateOnly(2026, 12, 21) };
        Almanac golden = At(recipe, TimeOfDay.GoldenHour);
        Assert.AreEqual(12.0, golden.SolarHours);
        Assert.IsTrue(golden.Sun.ElevationDegrees is < 0.0 and > -2.0, "the noon sun stays just below the horizon");
        StringAssert.Contains(golden.Hints.Single(), "never climbs to +2° at Lofoten on 2026-12-21 (polar night)");
        foreach (TimeOfDay time in new[] { TimeOfDay.Dawn, TimeOfDay.Dusk })
        {
            Almanac almanac = At(recipe, time);
            Assert.AreEqual(0, almanac.Hints.Count, $"{time}: the polar night's noon twilight still reaches −4°");
            Assert.AreEqual(-4.0, almanac.Sun.TrueElevationDegrees, 1e-6);
        }
        Assert.AreEqual(0, At(recipe, TimeOfDay.Night).Hints.Count);
    }

    [TestMethod]
    public void APinnedSunElevation_ReplacesThePresetsHeight()
    {
        var recipe = new Recipe { Location = Place.Zermatt, Season = Season.Autumn, SunElevation = 2.0 };
        Almanac evening = At(recipe, TimeOfDay.GoldenHour);
        Almanac morning = At(recipe, TimeOfDay.Dawn);
        Assert.AreEqual(2.0, evening.Sun.TrueElevationDegrees, 1e-6);
        Assert.IsTrue(evening.SolarHours > 12.0 && morning.SolarHours < 12.0, "rising for dawn, setting otherwise");
        Assert.AreEqual("pinned sunElevation +2°, setting", evening.SolarTimeFrom);
        Almanac never = At(recipe with { SunElevation = 60.0 }, TimeOfDay.Day);
        Assert.AreEqual(12.0, never.SolarHours);
        StringAssert.Contains(never.Hints.Single(), "the pinned sun elevation uses its highest");
    }

    [TestMethod]
    [DataRow(17.5, "17:30:00")]
    [DataRow(0.0, "00:00:00")]
    [DataRow(24.0, "24:00:00")]
    [DataRow(6.0 + 30.0 / 60.0 + 53.4 / 3600.0, "06:30:53")]
    [DataRow(6.0 + 59.0 / 60.0 + 59.6 / 3600.0, "07:00:00")]
    public void FormatHours_RoundsToSeconds(double hours, string expected) => Assert.AreEqual(expected, Almanac.FormatHours(hours));

    [TestMethod]
    public void Places_HaveSaneFacts()
    {
        CollectionAssert.AreEquivalent(
            Enum.GetValues<Place>().Where(p => p != Place.None).ToArray(),
            Places.All.Select(p => p.Place).ToArray(),
            "every dropdown place has an entry, once");
        foreach (PlaceInfo place in Places.All)
        {
            Assert.IsTrue(place.LatitudeDegrees is >= -90.0 and <= 90.0 && place.LatitudeDegrees != 0.0, place.Name);
            Assert.IsTrue(place.ValleyAltitudeMetres is >= 0.0 and < 3000.0, place.Name);
            Assert.IsFalse(place.IsCustom);
        }
        Assert.AreSame(Places.Get(Places.Default), Places.Get(Place.None));
        Assert.AreEqual((46.02, 1608.0), (Places.Get(Place.Zermatt).LatitudeDegrees, Places.Get(Place.Zermatt).ValleyAltitudeMetres));
        CollectionAssert.AreEquivalent(
            new[] { Place.TorresDelPaine, Place.Fiordland },
            Places.All.Where(p => p.Southern).Select(p => p.Place).ToArray());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Places.Custom(95.0));
    }

    private static Almanac At(Recipe recipe, TimeOfDay time) => Almanac.For(recipe with { Time = time });
}
