using Vista.Astronomy;
using Vista.Recipes;

namespace Vista.Tests;

/// <summary>
/// A recipe resolved to a sun (vista-plan §3.5, §10 M1.2): the place, the season's date or an override, and the
/// time-of-day presets relative to that day's sunrise and sunset, with polar day and night handled.
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
    public void Presets_AreRelativeToSunriseAndSunset()
    {
        var recipe = new Recipe { Location = Place.Zermatt, Season = Season.Autumn };
        Daylight daylight = SolarPosition.DaylightOn(46.02, new DateOnly(SeasonCalendar.ReferenceYear, 10, 15));

        Assert.AreEqual(daylight.SunriseHours - 20.0 / 60.0, At(recipe, TimeOfDay.Dawn).SolarHours, 1e-12);
        Assert.AreEqual(12.0, At(recipe, TimeOfDay.Day).SolarHours);
        Assert.AreEqual(daylight.SunsetHours - 1.0, At(recipe, TimeOfDay.GoldenHour).SolarHours, 1e-12);
        Assert.AreEqual(daylight.SunsetHours + 20.0 / 60.0, At(recipe, TimeOfDay.Dusk).SolarHours, 1e-12);
        Assert.AreEqual(0.0, At(recipe, TimeOfDay.Night).SolarHours);
        Assert.AreEqual("dawn: sunrise − 20 min", At(recipe, TimeOfDay.Dawn).SolarTimeFrom);
        Assert.AreEqual("goldenHour: sunset − 60 min", At(recipe, TimeOfDay.GoldenHour).SolarTimeFrom);
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
        Assert.IsTrue(golden is > 5.0 and < 12.0, $"golden hour at {golden:0.00}°");
        Assert.IsTrue(At(recipe, TimeOfDay.Dawn).Sun.AzimuthDegrees is > 90.0 and < 120.0, "dawn in the east-southeast");
        Assert.IsTrue(At(recipe, TimeOfDay.Dusk).Sun.AzimuthDegrees is > 240.0 and < 270.0, "dusk in the west-southwest");
        Assert.IsTrue(At(recipe, TimeOfDay.Night).Sun.ElevationDegrees < -18.0, "solar midnight is astronomical night");
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
        Assert.AreEqual("recipe solarTime", almanac.SolarTimeFrom);
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
    public void PolarDay_SendsTheLowSunPresetsToMidnight_WithAHint()
    {
        var recipe = new Recipe { Location = Place.Lofoten, Date = new DateOnly(2026, 6, 21) };
        Assert.AreEqual(DaylightKind.PolarDay, At(recipe, TimeOfDay.Day).Daylight.Kind);
        foreach (TimeOfDay time in new[] { TimeOfDay.Dawn, TimeOfDay.GoldenHour, TimeOfDay.Dusk })
        {
            Almanac almanac = At(recipe, time);
            Assert.AreEqual(0.0, almanac.SolarHours, $"{time}");
            Assert.IsTrue(almanac.Sun.ElevationDegrees > 0.0, "the midnight sun is up");
            StringAssert.Contains(almanac.Hints.Single(), "Polar day at Lofoten on 2026-06-21");
        }
        Assert.AreEqual(0, At(recipe, TimeOfDay.Day).Hints.Count);
        Assert.AreEqual(0, At(recipe, TimeOfDay.Night).Hints.Count, "night is solar midnight anyway");
    }

    [TestMethod]
    public void PolarNight_SendsTheDaytimePresetsToNoon_WithAHint()
    {
        var recipe = new Recipe { Location = Place.Lofoten, Date = new DateOnly(2026, 12, 21) };
        foreach (TimeOfDay time in new[] { TimeOfDay.Dawn, TimeOfDay.GoldenHour, TimeOfDay.Dusk })
        {
            Almanac almanac = At(recipe, time);
            Assert.AreEqual(12.0, almanac.SolarHours, $"{time}");
            Assert.IsTrue(almanac.Sun.ElevationDegrees is < 0.0 and > -2.0, "the noon sun stays just below the horizon");
            StringAssert.Contains(almanac.Hints.Single(), "Polar night");
        }
        Assert.AreEqual(0, At(recipe, TimeOfDay.Night).Hints.Count);
    }

    [TestMethod]
    public void Presets_StayInsideTheDay()
    {
        var shortDay = new Daylight(DaylightKind.RisesAndSets, 11.5, 12.5);
        Assert.AreEqual(12.0, Almanac.SolarHoursFor(TimeOfDay.GoldenHour, shortDay).Hours, "golden hour is never before noon");
        var longDay = new Daylight(DaylightKind.RisesAndSets, 0.1, 23.9);
        Assert.AreEqual(0.0, Almanac.SolarHoursFor(TimeOfDay.Dawn, longDay).Hours);
        Assert.AreEqual(24.0, Almanac.SolarHoursFor(TimeOfDay.Dusk, longDay).Hours);
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
