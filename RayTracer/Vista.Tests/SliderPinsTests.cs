using System.Text.Json;
using Vista.Astronomy;
using Vista.Recipes;
using Vista.Sliders;

namespace Vista.Tests;

/// <summary>The slider file (vista-plan §10 M1.11): its JSON, its validation, pins and resets, and a recipe's own pins.</summary>
[TestClass]
public sealed class SliderPinsTests
{
    [TestMethod]
    public void AnEmptyFile_PinsNothing()
    {
        SliderPins pins = VistaJson.ParseSliders("{}");
        Assert.AreEqual(SliderPins.None, pins);
        Assert.AreEqual(0, pins.Pinned.Count);
        Assert.AreEqual(TemperatureUnit.Celsius, pins.TemperatureUnit);
    }

    [TestMethod]
    public void EveryField_ReadsAndRoundTrips()
    {
        const string json = """
            {
              "schema": 1, "location": "lofoten", "altitude": 300, "season": "autumn", "date": "2026-10-01", "time": "goldenHour",
              "sunElevation": 2.5, "temperature": -3.5, "temperatureUnit": "fahrenheit", "humidity": 80, "snowline": 1200, "visibility": 40
            }
            """;
        SliderPins pins = VistaJson.ParseSliders(json);
        Assert.AreEqual(Place.Lofoten, pins.Location);
        Assert.AreEqual(new DateOnly(2026, 10, 1), pins.Date);
        Assert.AreEqual(TimeOfDay.GoldenHour, pins.Time);
        Assert.AreEqual(-3.5, pins.Temperature);
        CollectionAssert.AreEqual(
            new[] { Slider.Location, Slider.Altitude, Slider.Season, Slider.Date, Slider.Time, Slider.SolarTime, Slider.Temperature, Slider.Humidity, Slider.Snow, Slider.Haze },
            pins.Pinned.ToArray());
        Assert.AreEqual(pins, VistaJson.ParseSliders(VistaJson.Serialize(pins)));
        StringAssert.Contains(VistaJson.Serialize(pins), "\"snowline\": 1200");
        Assert.IsFalse(VistaJson.Serialize(pins).Contains("pinned", StringComparison.Ordinal), "derived members stay out of the file");
    }

    [TestMethod]
    [DataRow("""{ "location": "lofoten", "latitude": 60 }""", "not both")]
    [DataRow("""{ "solarTime": "17:00", "sunElevation": 4 }""", "not both")]
    [DataRow("""{ "altitude": 9000 }""", "altitude must be in [0, 8000]")]
    [DataRow("""{ "temperature": 70 }""", "temperature must be in [-60, 50]")]
    [DataRow("""{ "humidity": 101 }""", "humidity must be in [0, 100]")]
    [DataRow("""{ "snowline": -5 }""", "snowline must be in [0, 12000]")]
    [DataRow("""{ "visibility": 0.5 }""", "visibility must be in [1, 500]")]
    [DataRow("""{ "schema": 2 }""", "Unsupported slider file schema 2")]
    [DataRow("""{ "temperatureUnit": "none" }""", "celsius or fahrenheit")]
    public void InvalidValues_AreListed(string json, string message)
    {
        InvalidDataException e = Assert.ThrowsExactly<InvalidDataException>(() => VistaJson.ParseSliders(json));
        StringAssert.Contains(e.Message, message);
    }

    [TestMethod]
    public void UnknownFields_AreRejected() =>
        Assert.ThrowsExactly<JsonException>(() => VistaJson.ParseSliders("""{ "snowfall": 3 }"""));

    [TestMethod]
    public void EverySlider_HasAFieldThatPinsIt_AndAReset()
    {
        foreach (Slider slider in Enum.GetValues<Slider>().Where(s => s != Slider.None))
        {
            Assert.IsTrue(SliderPins.Fields.Values.Contains(slider), $"{slider} has a field");
            Assert.IsFalse(SliderPins.None.Reset(slider).IsPinned(slider));
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SliderPins.None.IsPinned(Slider.None));
    }

    [TestMethod]
    public void ARecipesOwnFields_PinTheirSliders_ItsDateDecidingTheSeason()
    {
        var recipe = new Recipe { Time = TimeOfDay.Dusk, Season = Season.Winter, Location = Place.Banff, Date = new DateOnly(2026, 7, 1), Altitude = 2000.0 };
        Assert.IsTrue(recipe.HasPins);
        SliderPins pins = SliderPins.FromRecipe(recipe);
        Assert.AreEqual(TimeOfDay.Dusk, pins.Time);
        Assert.AreEqual(Place.Banff, pins.Location);
        Assert.AreEqual(2000.0, pins.Altitude);
        Assert.IsNull(pins.Season, "a recipe's date has replaced its season since M1.2");
        Assert.AreEqual(SliderPins.None, SliderPins.FromRecipe(new Recipe { SetPieces = [SetPiece.AlpineRange] }));
        Assert.IsFalse(new Recipe().HasPins);
        Assert.IsFalse(VistaJson.Serialize(recipe).Contains("hasPins", StringComparison.Ordinal));
    }
}
