using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Recipes;
using Vista.Sliders;
using Vista.Weather;

namespace Vista.Tests;

/// <summary>
/// The linked sliders and the weather chain (vista-plan §3.4, §3.6, §10 M1.11): every slider auto or pinned, pinning one stops only
/// that one, reset re-links, set pieces push defaults, contradictions hint and never block, and the chain runs from the place and
/// the date to the temperature, humidity, freezing level, snow and haze.
/// </summary>
[TestClass]
public sealed class LinkedSlidersTests
{
    private static readonly Recipe Mountains = new() { SetPieces = [SetPiece.SnowCappedMountains] };

    private static SliderState Resolve(SliderPins pins, Recipe? recipe = null) => LinkedSliders.Resolve(recipe ?? Mountains, pins);

    [TestMethod]
    public void NoPins_EverySliderIsAuto_AtTheDefaults()
    {
        SliderState state = Resolve(SliderPins.None);
        CollectionAssert.AreEquivalent(Enum.GetValues<Slider>().Where(s => s != Slider.None).ToArray(), state.Readings.Keys.ToArray(), "every slider is read");
        Assert.IsTrue(state.Readings.Values.All(r => !r.Pinned && r.From.StartsWith("auto: ", StringComparison.Ordinal)));
        Assert.AreEqual("zermatt", state.Readings[Slider.Location].Value);
        Assert.AreEqual(1608.0, state.BaseAltitudeMetres);
        Assert.AreEqual(Season.Summer, state.Almanac.Season);
        Assert.AreEqual(new DateOnly(2026, 7, 15), state.Almanac.Date);
        Assert.AreEqual(12.0, state.Almanac.SolarHours);
        Assert.AreEqual(0, state.Hints.Count);
        // Zermatt's July noon at its valley floor: the normals' day, 30 m below the station, two hours before the high.
        Assert.IsTrue(state.TemperatureCelsius is > 15.0 and < 20.0, $"{state.TemperatureCelsius:0.0} °C");
        Assert.IsTrue(state.HumidityPercent is > 35.0 and < 75.0, $"{state.HumidityPercent:0} %");
        Assert.AreEqual(state.BaseAltitudeMetres + state.TemperatureCelsius / Lapse.RateCelsiusPerMetre, state.FreezingLevelMetres, 1e-9);
        Assert.AreEqual(state.ClimateSnowlineMetres, state.SnowlineMetres, "nothing moved the season's snowline");
        Assert.IsTrue(state.VisibilityKm is > 50.0 and < 250.0, $"{state.VisibilityKm:0} km");
    }

    [TestMethod]
    public void PinningTheTemperature_StillLetsTheSnowHumidityAndHazeFollow()
    {
        SliderState auto = Resolve(SliderPins.None);
        SliderState cold = Resolve(new SliderPins { Temperature = -10.0 });
        Assert.IsTrue(cold.Readings[Slider.Temperature].Pinned);
        Assert.AreEqual(-10.0, cold.TemperatureCelsius);
        Assert.IsFalse(cold.Readings[Slider.Snow].Pinned, "only the temperature is pinned");
        double expected = auto.ClimateSnowlineMetres + LinkedSliders.SnowFollowsFreezingLevel * (-10.0 - auto.TemperatureCelsius) / Lapse.RateCelsiusPerMetre;
        Assert.AreEqual(Math.Max(0.0, expected), cold.SnowlineMetres, 1e-6, "the snowline moves half as far as the freezing level");
        Assert.IsTrue(cold.SnowlineMetres < auto.SnowlineMetres - 1000.0, "a cold snap brings the snow down");
        Assert.AreEqual(100.0, cold.HumidityPercent, 1e-9, "air colder than the day's dew point is saturated");
        Assert.AreNotEqual(auto.AerosolOpticalDepth550, cold.AerosolOpticalDepth550, "the haze follows the humidity");
        Assert.AreEqual(auto.Almanac.Sun, cold.Almanac.Sun, "nothing upstream moves");
    }

    [TestMethod]
    public void PinningOneSlider_StopsOnlyThatOne()
    {
        SliderState auto = Resolve(SliderPins.None);
        SliderState humid = Resolve(new SliderPins { Humidity = 90.0 });
        Assert.AreEqual(auto.TemperatureCelsius, humid.TemperatureCelsius, "the temperature is upstream of the humidity");
        Assert.AreEqual(auto.SnowlineMetres, humid.SnowlineMetres);
        Assert.AreEqual(Haze.OpticalDepth(90.0), humid.AerosolOpticalDepth550, 1e-12, "the haze grows by the pinned humidity");

        SliderState clear = Resolve(new SliderPins { Visibility = 200.0 });
        Assert.AreEqual(200.0, clear.VisibilityKm);
        Assert.AreEqual(Haze.OpticalDepthForVisibility(AtmosphereModel.Default, 200.0, 1608.0), clear.AerosolOpticalDepth550, 1e-12);
        Assert.AreEqual(auto.HumidityPercent, clear.HumidityPercent, "pinning the haze leaves the humidity auto");
        Assert.AreEqual(200.0, Haze.VisibilityKm(clear.Air(AtmosphereModel.Default), 1608.0), 1e-6, "the render's air has the pinned visibility");
    }

    [TestMethod]
    public void Reset_ReLinksASlider()
    {
        var pins = new SliderPins { Temperature = 25.0, Snowline = 900.0, Location = Place.Banff, SolarTime = new TimeOnly(9, 0) };
        SliderState auto = Resolve(SliderPins.None);
        foreach (Slider slider in new[] { Slider.Temperature, Slider.Snow, Slider.Location, Slider.SolarTime })
        {
            Assert.IsTrue(pins.IsPinned(slider));
            Assert.IsFalse(pins.Reset(slider).IsPinned(slider), $"{slider} reset");
        }
        SliderPins reset = pins.Reset(Slider.Temperature).Reset(Slider.Snow).Reset(Slider.Location).Reset(Slider.SolarTime);
        Assert.AreEqual(SliderPins.None, reset, "every pin reset is no pins");
        SliderState relinked = Resolve(reset);
        Assert.AreEqual(auto.TemperatureCelsius, relinked.TemperatureCelsius);
        Assert.AreEqual(auto.SnowlineMetres, relinked.SnowlineMetres);
        Assert.AreEqual(auto.Almanac.Sun, relinked.Almanac.Sun);
        Assert.AreEqual(auto.Almanac.Date, relinked.Almanac.Date);
        Assert.AreEqual(0, Resolve(SliderPins.None.Reset(Slider.Haze)).Pins.Pinned.Count, "resetting an auto slider changes nothing");
    }

    [TestMethod]
    public void AFrozenLake_PushesWinterAndFrost_OntoAutoSliders()
    {
        var recipe = Mountains.With(SetPiece.FrozenLake);
        SliderState state = Resolve(SliderPins.None, recipe);
        Assert.AreEqual(Season.Winter, state.Almanac.Season);
        Assert.AreEqual("auto: pushed by frozenLake", state.Readings[Slider.Season].From);
        Assert.IsTrue(state.TemperatureCelsius <= SliderPushes.FrozenLakeCeilingCelsius, $"{state.TemperatureCelsius} °C");
        Assert.AreEqual(0, state.Implausible.Count);

        // Yosemite's winter noon is mild; the frozen lake lowers it, and the snow follows the frost down.
        SliderState plain = Resolve(new SliderPins { Location = Place.Yosemite, Season = Season.Winter });
        SliderState frozen = Resolve(new SliderPins { Location = Place.Yosemite, Season = Season.Winter }, recipe);
        Assert.IsTrue(plain.TemperatureCelsius > 5.0, $"{plain.TemperatureCelsius} °C");
        Assert.AreEqual(SliderPushes.FrozenLakeCeilingCelsius, frozen.TemperatureCelsius);
        StringAssert.Contains(frozen.Readings[Slider.Temperature].From, "pushed by frozenLake");
        Assert.IsTrue(frozen.SnowlineMetres < plain.SnowlineMetres, "the snow follows the push");
    }

    [TestMethod]
    public void Contradictions_RenderAsAsked_WithAnImplausibleHint()
    {
        var recipe = Mountains.With(SetPiece.FrozenLake);
        SliderState warm = Resolve(new SliderPins { Temperature = 15.0 }, recipe);
        Assert.AreEqual(15.0, warm.TemperatureCelsius, "a pin is rendered as asked");
        Assert.IsTrue(warm.Implausible.Any(h => h.Contains("a frozen lake with the valley at 15.0 °C", StringComparison.Ordinal)), string.Join(" | ", warm.Implausible));
        StringAssert.Contains(Resolve(new SliderPins { Season = Season.Summer }, recipe).Implausible.Single(), "a frozen lake in summer");

        SliderState clash = Resolve(new SliderPins { Season = Season.Winter, Date = new DateOnly(2026, 7, 15) });
        Assert.AreEqual(Season.Winter, clash.Almanac.Season);
        Assert.AreEqual(new DateOnly(2026, 7, 15), clash.Almanac.Date);
        StringAssert.Contains(clash.Implausible.Single(), "2026-07-15 is summer at Zermatt, but the season is pinned to winter");

        SliderState snow = Resolve(new SliderPins { Snowline = 0.0 });
        StringAssert.Contains(snow.Implausible.Single(), "seasonal snow down to 0 m");
        Assert.IsTrue(snow.Hints.Contains(snow.Implausible.Single()), "the hints carry it to the warnings");
    }

    [TestMethod]
    public void AutumnAtTorresDelPaine_IsApril_AndTheDateDecidesTheSeason()
    {
        SliderState autumn = Resolve(new SliderPins { Location = Place.TorresDelPaine, Season = Season.Autumn });
        Assert.AreEqual(4, autumn.Almanac.Date.Month);
        Assert.AreEqual("auto: autumn in the southern hemisphere (the 15th of its middle month)", autumn.Readings[Slider.Date].From);

        SliderState dated = Resolve(new SliderPins { Location = Place.TorresDelPaine, Date = new DateOnly(2026, 4, 1) });
        Assert.AreEqual(Season.Autumn, dated.Almanac.Season);
        Assert.IsFalse(dated.Readings[Slider.Season].Pinned);
        Assert.AreEqual("auto: the pinned date's, in the southern hemisphere", dated.Readings[Slider.Season].From);
        Assert.IsTrue(autumn.TemperatureCelsius < Resolve(new SliderPins { Location = Place.TorresDelPaine, Season = Season.Summer }).TemperatureCelsius);
    }

    [TestMethod]
    public void Lofoten_IsMilderThanItsLatitudeModel()
    {
        foreach (Season season in new[] { Season.Winter, Season.Autumn })
        {
            SliderState lofoten = Resolve(new SliderPins { Location = Place.Lofoten, Season = season, Altitude = 0.0 });
            SliderState latitude = Resolve(new SliderPins { Latitude = Places.Get(Place.Lofoten).LatitudeDegrees, Season = season });
            Assert.AreEqual("latitude model", latitude.Climate.Station);
            Assert.IsTrue(lofoten.TemperatureCelsius > latitude.TemperatureCelsius + 5.0,
                $"{season}: Lofoten {lofoten.TemperatureCelsius:0.0} °C against its latitude's {latitude.TemperatureCelsius:0.0} °C");
            Assert.IsTrue(lofoten.SnowlineMetres > latitude.SnowlineMetres, $"{season}: less snow at the coast");
        }
    }

    [TestMethod]
    public void TheDayRunsFromAColdHumidDawn_ToAWarmDryAfternoon()
    {
        SliderState dawn = Resolve(new SliderPins { Time = TimeOfDay.Dawn });
        SliderState afternoon = Resolve(new SliderPins { SolarTime = new TimeOnly(14, 0) });
        Assert.IsTrue(dawn.TemperatureCelsius < afternoon.TemperatureCelsius - 8.0);
        Assert.AreEqual(afternoon.Day.HighCelsius, afternoon.TemperatureCelsius, 1e-9, "the high at 14:00");
        Assert.IsTrue(dawn.HumidityPercent > 90.0, $"dawn at {dawn.HumidityPercent:0} %, its dew point the day's low");
        Assert.IsTrue(afternoon.HumidityPercent < 60.0, $"afternoon at {afternoon.HumidityPercent:0} %");
        Assert.AreEqual(dawn.AerosolOpticalDepth550, afternoon.AerosolOpticalDepth550, "the haze is the day's, not the hour's");
        Assert.AreEqual(dawn.SnowlineMetres, afternoon.SnowlineMetres, "snow is history, not the hour");
    }

    [TestMethod]
    public void TheSeasons_MoveTheSnowlineAndTheTemperature_FromTheDefaultsAlone()
    {
        double[] snowlines = [.. Enum.GetValues<Season>().Select(s => Resolve(new SliderPins { Season = s }).SnowlineMetres)];
        double spring = snowlines[(int)Season.Spring], summer = snowlines[(int)Season.Summer];
        double autumn = snowlines[(int)Season.Autumn], winter = snowlines[(int)Season.Winter];
        Assert.IsTrue(winter < spring && spring < autumn && autumn < summer, $"winter {winter:0}, spring {spring:0}, autumn {autumn:0}, summer {summer:0} m");
        Assert.IsTrue(winter <= 1608.0, "winter snow on Zermatt's valley floor");
    }

    [TestMethod]
    public void APinnedAltitude_CoolsTheValley_ButLeavesTheSnowlineWhereTheClimatePutsIt()
    {
        SliderState valley = Resolve(SliderPins.None);
        SliderState high = Resolve(new SliderPins { Altitude = 2608.0 });
        Assert.AreEqual(valley.TemperatureCelsius - 6.5, high.TemperatureCelsius, 1e-9);
        Assert.AreEqual(valley.FreezingLevelMetres, high.FreezingLevelMetres, 1e-6);
        Assert.AreEqual(valley.SnowlineMetres, high.SnowlineMetres, "the snowline is an altitude above sea level");
    }

    [TestMethod]
    public void APinnedVisibilityBeyondCleanAir_RendersCleanAir_WithANote()
    {
        SliderState state = Resolve(new SliderPins { Visibility = 500.0, Altitude = 0.0 });
        Assert.AreEqual(0.0, state.AerosolOpticalDepth550);
        StringAssert.Contains(state.Notes.Single(), "Clean air limits the view");
        Assert.AreEqual(0, state.Implausible.Count);
    }

    [TestMethod]
    public void Fahrenheit_IsOnlyHowTheTemperatureIsShown()
    {
        SliderState state = Resolve(new SliderPins { Temperature = 5.0, TemperatureUnit = TemperatureUnit.Fahrenheit });
        Assert.AreEqual(5.0, state.TemperatureCelsius);
        Assert.AreEqual("41.0 °F (5.0 °C)", state.Readings[Slider.Temperature].Value);
        Assert.AreEqual(5.0, state.Readings[Slider.Temperature].Number);
    }

    [TestMethod]
    public void Resolving_IsPure()
    {
        var pins = new SliderPins { Location = Place.Hokkaido, Season = Season.Autumn, Time = TimeOfDay.GoldenHour, Humidity = 70.0 };
        SliderState a = Resolve(pins), b = Resolve(pins);
        Assert.AreEqual(a.Almanac.Sun, b.Almanac.Sun);
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.SnowlineMetres), BitConverter.DoubleToInt64Bits(b.SnowlineMetres));
        Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.AerosolOpticalDepth550), BitConverter.DoubleToInt64Bits(b.AerosolOpticalDepth550));
        CollectionAssert.AreEqual(a.Readings.OrderBy(r => r.Key).ToArray(), b.Readings.OrderBy(r => r.Key).ToArray());
    }
}
