using Vista.Astronomy;
using Vista.Recipes;
using Vista.Sliders;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// Where the world stands (vista-plan §10 M1.12c verify: "the anchor (the 5th percentile at the valley floor for any seed; a pinned
/// altitude untouched; deterministic)").
/// </summary>
[TestClass]
public sealed class FootingTests
{
    private static readonly Recipe Mountains = new() { SetPieces = [SetPiece.AlpineRange] };

    [TestMethod]
    public void TheFooting_IsTheExactPercentile()
    {
        // Heights 0..n−1 in a scrambled order: the 5th percentile is the height of rank round(0.05·(n − 1)).
        const int size = 65;
        int count = size * size;
        var heights = new float[count];
        for (int k = 0; k < count; k++)
            heights[k] = (k * 2654435761u % (uint)count) * 0.5f;
        var field = new HeightField(size, 8.0, heights);
        WorldFooting footing = WorldFooting.Of(field);
        Assert.AreEqual(WorldFooting.DefaultPercentile, footing.Percentile);
        Assert.AreEqual(Math.Round(0.05 * (count - 1)) * 0.5, footing.HeightMetres);
        Assert.AreEqual(0.0, footing.LowestMetres);
        Assert.AreEqual(0.0, WorldFooting.Of(field, 0.0).HeightMetres);
        Assert.AreEqual((count - 1) * 0.5, WorldFooting.Of(field, 100.0).HeightMetres);
        Assert.AreEqual(footing, WorldFooting.Of(field), "a pure function of the heights");
        Assert.AreEqual(12.5, WorldFooting.Of(new HeightField(3, 8.0, Enumerable.Repeat(12.5f, 9).ToArray())).HeightMetres, "a flat world");
    }

    [TestMethod]
    public void TheAutoAltitude_PutsTheWorldsLowestValleysAtThePlacesValleyFloor()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4 })
        {
            TerrainWorld world = WorldBuilder.Build(Mountains, seed, new WorldGrid(129, 64.0), null, null);
            SliderState state = LinkedSliders.Resolve(Mountains, SliderPins.None, footing: world.Footing);
            double valley = Places.Get(Place.Zermatt).ValleyAltitudeMetres;
            Assert.AreEqual(valley, state.BaseAltitudeMetres + world.Footing.HeightMetres, 1e-9, $"seed {seed}");
            Assert.AreEqual(valley, state.ValleyFloorMetres, 1e-9);
            Assert.AreEqual(world.Footing.HeightMetres, state.FootingMetres);
            // A twentieth of the world lies below the place's valley floor.
            double below = world.Surface.Heights.ToArray().Count(h => state.BaseAltitudeMetres + h < valley) / (double)world.Surface.Heights.Length;
            Assert.AreEqual(0.05, below, 0.001, $"seed {seed}: below the valley floor");
            StringAssert.Contains(state.Readings[Slider.Altitude].From, "5th percentile");
            Assert.IsFalse(state.Readings[Slider.Altitude].Pinned);
        }
    }

    [TestMethod]
    public void APinnedAltitude_StaysPinned_AndTheWeatherIsReadAtTheValleyFloor()
    {
        var footing = new WorldFooting(5.0, 400.0, 150.0);
        SliderState pinned = LinkedSliders.Resolve(Mountains, new SliderPins { Altitude = 1000.0 }, footing: footing);
        Assert.AreEqual(1000.0, pinned.BaseAltitudeMetres, "the pin is the base altitude, height 0");
        Assert.AreEqual(1400.0, pinned.ValleyFloorMetres);
        Assert.AreEqual("pinned", pinned.Readings[Slider.Altitude].From);
        // The weather is the valley floor's: as without a world at the valley floor's altitude.
        SliderState plain = LinkedSliders.Resolve(Mountains, new SliderPins { Altitude = 1400.0 });
        Assert.AreEqual(plain.TemperatureCelsius, pinned.TemperatureCelsius, 1e-9);
        Assert.AreEqual(plain.HumidityPercent, pinned.HumidityPercent, 1e-9);
        Assert.AreEqual(plain.FreezingLevelMetres, pinned.FreezingLevelMetres, 1e-9);
        Assert.AreEqual(plain.VisibilityKm, pinned.VisibilityKm, 1e-9);
        // The auto weather is the place's whatever the footing: the valley floor stays where the place's is.
        SliderState auto = LinkedSliders.Resolve(Mountains, SliderPins.None, footing: footing);
        SliderState before = LinkedSliders.Resolve(Mountains, SliderPins.None);
        Assert.AreEqual(1208.0, auto.BaseAltitudeMetres, 1e-9);
        Assert.AreEqual(before.TemperatureCelsius, auto.TemperatureCelsius, 1e-9);
        Assert.AreEqual(before.FreezingLevelMetres, auto.FreezingLevelMetres, 1e-9);
        Assert.AreEqual(before.SnowlineMetres, auto.SnowlineMetres, 1e-9);
        Assert.AreEqual(0.0, before.FootingMetres, "without a world, height 0 is the valley floor (before M1.12c)");
        Assert.AreEqual(before.BaseAltitudeMetres, before.ValleyFloorMetres);
    }

    [TestMethod]
    public void TheAnchor_IsHeldAtSeaLevel()
    {
        // Lofoten's valley floor is 10 m: a world whose valleys stand 400 m above its height 0 cannot sink its height 0 below the sea.
        var footing = new WorldFooting(5.0, 400.0, 150.0);
        SliderState state = LinkedSliders.Resolve(Mountains, new SliderPins { Location = Place.Lofoten }, footing: footing);
        Assert.AreEqual(0.0, state.BaseAltitudeMetres);
        Assert.AreEqual(400.0, state.ValleyFloorMetres);
        StringAssert.Contains(state.Readings[Slider.Altitude].From, "held at sea level");
    }
}
