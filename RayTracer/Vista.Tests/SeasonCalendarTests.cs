using Vista.Astronomy;
using Vista.Recipes;

namespace Vista.Tests;

/// <summary>Season ↔ date by hemisphere (vista-plan §3.5): autumn at Torres del Paine is April.</summary>
[TestClass]
public sealed class SeasonCalendarTests
{
    [TestMethod]
    [DataRow(Season.Spring, 4, 10)]
    [DataRow(Season.Summer, 7, 1)]
    [DataRow(Season.Autumn, 10, 4)]
    [DataRow(Season.Winter, 1, 7)]
    public void DateFor_IsMidSeason_SixMonthsApartAcrossTheEquator(Season season, int northMonth, int southMonth)
    {
        Assert.AreEqual(new DateOnly(SeasonCalendar.ReferenceYear, northMonth, 15), SeasonCalendar.DateFor(season, 46.02));
        Assert.AreEqual(new DateOnly(SeasonCalendar.ReferenceYear, southMonth, 15), SeasonCalendar.DateFor(season, -51.08));
    }

    [TestMethod]
    public void AutumnAtTorresDelPaine_IsApril()
    {
        PlaceInfo torres = Places.Get(Place.TorresDelPaine);
        Assert.AreEqual(4, SeasonCalendar.DateFor(Season.Autumn, torres.LatitudeDegrees).Month);
        Assert.AreEqual(10, SeasonCalendar.DateFor(Season.Autumn, Places.Get(Place.Zermatt).LatitudeDegrees).Month);
    }

    [TestMethod]
    public void SeasonOf_InvertsDateFor_InBothHemispheres()
    {
        foreach (Season season in Enum.GetValues<Season>())
        {
            foreach (double latitude in new[] { 46.02, 0.0, -51.08 })
                Assert.AreEqual(season, SeasonCalendar.SeasonOf(SeasonCalendar.DateFor(season, latitude), latitude), $"{season} at {latitude}");
        }
    }

    [TestMethod]
    [DataRow(2026, 3, 1, 10.0, Season.Spring)]
    [DataRow(2026, 5, 31, 10.0, Season.Spring)]
    [DataRow(2026, 6, 1, 10.0, Season.Summer)]
    [DataRow(2026, 12, 1, 10.0, Season.Winter)]
    [DataRow(2026, 2, 28, 10.0, Season.Winter)]
    [DataRow(2026, 1, 15, -10.0, Season.Summer)]
    [DataRow(2026, 4, 15, -10.0, Season.Autumn)]
    [DataRow(2026, 9, 1, -10.0, Season.Spring)]
    [DataRow(2026, 1, 15, 0.0, Season.Winter)]
    public void SeasonOf_UsesMeteorologicalSeasons(int year, int month, int day, double latitude, Season expected) =>
        Assert.AreEqual(expected, SeasonCalendar.SeasonOf(new DateOnly(year, month, day), latitude));
}
