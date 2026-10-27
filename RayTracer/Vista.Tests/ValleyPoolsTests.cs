using System.Globalization;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Cli;
using Vista.Recipes;
using Vista.Sliders;
using Vista.Weather;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The valleys' pools (vista-plan §10 M1.13b verify: "CI: the pool map (a closed basin fills, a steep open valley does not,
/// deterministic, cached with the world), the density (graded, continuous, the noise bounded), the mist chain (a humid autumn dawn
/// saturates, a dry summer one stays haze; every place's humidity normals sourced), the sunlight volume on a synthetic ridge").
/// </summary>
[TestClass]
public sealed class ValleyPoolsTests
{
    private static readonly Recipe Mountains = new() { SetPieces = [SetPiece.SnowCappedMountains] };

    /// <summary>The seed-42 world's footing (as the valley haze's tests take it).</summary>
    private static readonly WorldFooting World = new(WorldFooting.DefaultPercentile, 651.0, 392.0) { CrestMetres = 1785.0 };

    private static HeightField Field(int size, double cell, Func<int, int, float> height)
    {
        var heights = new float[size * size];
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
                heights[j * size + i] = height(i, j);
        }
        return new HeightField(size, cell, heights);
    }

    /// <summary>A valley running north to south down a gradient, its walls rising 0.4 a metre to either side, on a 257² grid of 16 m.</summary>
    private static HeightField Valley(double gradient) =>
        Field(257, 16.0, (i, j) => (float)(1000.0 + 0.4 * 16.0 * Math.Abs(i - 128) + gradient * 16.0 * j));

    private static SliderState At(Place place, Season season, double sunElevation, SliderPins? more = null) =>
        LinkedSliders.Resolve(Mountains with { Location = place, Season = season, Time = TimeOfDay.Dawn },
            (more ?? SliderPins.None) with { Location = place, Season = season, Time = TimeOfDay.Dawn, SunElevation = sunElevation },
            AtmosphereModel.Default, World);

    // ── The pool map ────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AFlatValley_HoldsItsPool_AndASteepOneDrainsIt()
    {
        PoolLevel Level(double gradient)
        {
            HeightField field = Valley(gradient);
            return PoolMap.Level(field, DrainageMap.Of(field), 4, basins: true);
        }

        // Along the valley's axis, away from the grid's edges.
        double Hold(PoolLevel level) => level.Sample(level.Hold, 0.0, 0.0);
        double flat = Hold(Level(0.01)), steep = Hold(Level(0.2));
        Assert.AreEqual(1.0 / (1.0 + Math.Pow(0.01 / PoolMap.HalfHoldGradient, 2.0)), flat, 0.02, "a 1% floor holds nearly all of it");
        Assert.AreEqual(1.0 / (1.0 + Math.Pow(0.2 / PoolMap.HalfHoldGradient, 2.0)), steep, 0.02, "a 20% floor drains most of it");
        Assert.IsTrue(steep < 0.15 && flat > 0.9, $"flat {flat:0.00}, steep {steep:0.00}");

        // Both slopes drain to the same floor: the pool's top lies level across the valley.
        PoolLevel level = Level(0.01);
        double west = level.Sample(level.Floor, -400.0, 0.0), axis = level.Sample(level.Floor, 0.0, 0.0), east = level.Sample(level.Floor, 400.0, 0.0);
        Assert.AreEqual(axis, west, 5.0, "the west slope's floor is the axis'");
        Assert.AreEqual(axis, east, 5.0, "the east slope's floor is the axis'");
        Assert.IsTrue(level.Sample(level.Cap, 0.0, 0.0) > 100.0, "the valley's walls allow a pool");
    }

    [TestMethod]
    public void AClosedBasin_FillsToItsRim()
    {
        // A bowl 60 m deep in a plain that slopes away to the grid's edge.
        HeightField field = Field(257, 16.0, (i, j) =>
        {
            double r = Math.Sqrt((i - 128.0) * (i - 128.0) + (j - 128.0) * (j - 128.0)) * 16.0;
            return (float)(r < 600.0 ? 1060.0 - 60.0 * Math.Cos(Math.PI * r / 1200.0) : 1060.0 - 0.05 * (r - 600.0));
        });
        PoolLevel level = PoolMap.Level(field, DrainageMap.Of(field), 4, basins: true);
        double floor = level.Sample(level.Floor, 0.0, 0.0);
        Assert.AreEqual(1060.0, floor, 2.0, "the bowl's floor is its water's level: its rim");
        Assert.AreEqual(1.0, level.Sample(level.Hold, 0.0, 0.0), 0.02, "a closed basin holds all of the night's pool");
        // Without basins (the far field's base terrain, whose hollows are noise) the bowl is no floor of its own: its air drains with
        // the land around it, down the plain's slope, which holds less.
        PoolLevel open = PoolMap.Level(field, DrainageMap.Of(field), 4, basins: false);
        Assert.IsTrue(open.Sample(open.Hold, 0.0, 0.0) < 0.9, $"the far field's hollow holds {open.Sample(open.Hold, 0.0, 0.0):0.00}");
    }

    [TestMethod]
    public void ThePoolMap_IsDeterministic_AndCachedWithTheWorld()
    {
        var cache = new WorldCache();
        TerrainWorld world = cache.World(Mountains, 42, TestWorlds.Grid, erode: false);
        PoolMap first = cache.Pools();
        Assert.AreSame(first, cache.Pools(), "cached with the world");
        Assert.AreEqual(2, first.Levels.Count, "the world's level and the far field's");
        Assert.AreEqual(world.Surface.Size, (first.Levels[0].Size - 1) * (int)(first.Levels[0].CellMetres / world.Surface.CellSize) + 1, "the world's level spans it");
        PoolMap again = PoolMap.For(world.Surface, DrainageMap.Of(world.Surface), PoolMap.FarTerrain(Mountains, 42, TestWorlds.Grid));
        Assert.AreEqual(first.ContentHash(), again.ContentHash(), "the same bits again");
        cache.World(Mountains, 7, TestWorlds.Grid, erode: false);
        Assert.AreNotSame(first, cache.Pools(), "another world, another map");
    }

    // ── Each valley's pool ──────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheScenesValley_KeepsM113asShare_AndAShallowerPoolBreaksSooner()
    {
        SliderState morning = LinkedSliders.Resolve(Mountains, new SliderPins
        {
            Location = Place.Zermatt, Date = new DateOnly(2026, 10, 15), SolarTime = new TimeOnly(9, 0),
        }, AtmosphereModel.Default, World);
        PoolWeather w = morning.Pools;
        Assert.IsTrue(w.Morning && w.HeatRatio is > 0.05 and < 1.0, $"a pool breaking up: {w.HeatRatio:0.00}");
        Assert.AreEqual(Math.Clamp(1.0 - w.HeatRatio, 0.0, 1.0), ValleyPools.Share(w, 1.0), 1e-12, "the full pool's share is M1.13a's");
        Assert.IsTrue(ValleyPools.Share(w, 0.6) < ValleyPools.Share(w, 1.0), "a shallower pool has less left");
        PoolCell full = ValleyPools.Cell(w, w.FloorMetres, 1.0, double.MaxValue);
        PoolCell held = ValleyPools.Cell(w, w.FloorMetres, 0.6, double.MaxValue);
        PoolCell capped = ValleyPools.Cell(w, w.FloorMetres, 1.0, 50.0);
        Assert.AreEqual(w.DawnDepthMetres * ValleyPools.Share(w, 1.0), full.DepthMetres, 1e-9, "the scene's valley's depth");
        Assert.IsTrue(held.DepthMetres < 0.6 * full.DepthMetres, "a valley holding 60% keeps less than 60% by mid-morning");
        Assert.IsTrue(capped.DepthMetres <= 50.0, "never deeper than its cap");
        // Pinned, every valley keeps the night's whole pool.
        PoolWeather pinned = w with { WholePool = true };
        Assert.AreEqual(0.6 * w.DawnDepthMetres, ValleyPools.Cell(pinned, w.FloorMetres, 0.6, double.MaxValue).DepthMetres, 1e-9);
    }

    [TestMethod]
    public void ThePoolsHaze_IsDensestAtTheFloor_AndThinsUpward()
    {
        SliderState dawn = At(Place.ScottishHighlands, Season.Autumn, 2.0);
        PoolCell c = ValleyPools.Cell(dawn.Pools, dawn.Pools.FloorMetres, 1.0, double.MaxValue);
        Assert.IsTrue(c.DepthMetres > 100.0, $"{c.DepthMetres:0} m");
        Assert.IsTrue(c.HazeFloor550 > c.HazeTop550 && c.HazeTop550 >= 0.0, $"{c.HazeFloor550 * 1000:0.000}/km at the floor, {c.HazeTop550 * 1000:0.000} at the top");
        // A higher valley holds less of the valley's own haze.
        PoolCell high = ValleyPools.Cell(dawn.Pools with { WholePool = true, MistOff = true }, dawn.Pools.FloorMetres + 800.0, 1.0, double.MaxValue);
        PoolCell low = ValleyPools.Cell(dawn.Pools with { WholePool = true, MistOff = true }, dawn.Pools.FloorMetres, 1.0, double.MaxValue);
        Assert.IsTrue(high.HazeFloor550 < low.HazeFloor550, "the haze thins with the floor's altitude");
    }

    [TestMethod]
    public void AHumidAutumnDawn_Saturates_AndADrySummerOneStaysHaze()
    {
        SliderState humid = At(Place.Fiordland, Season.Autumn, 2.0);
        PoolCell misty = ValleyPools.Cell(humid.Pools, humid.Pools.FloorMetres, 1.0, double.MaxValue);
        Assert.IsTrue(misty.MistMetres > 50.0 && misty.MistMetres <= misty.DepthMetres, $"Milford Sound's dawn: {misty.MistMetres:0} m of mist in {misty.DepthMetres:0} m");
        StringAssert.Contains(humid.Readings[Slider.Mist].Value, "m to see");
        StringAssert.Contains(humid.Readings[Slider.Mist].From, "humidity normals");

        SliderState dry = At(Place.Yosemite, Season.Summer, 2.0);
        PoolCell clear = ValleyPools.Cell(dry.Pools, dry.Pools.FloorMetres, 1.0, double.MaxValue);
        Assert.IsTrue(clear.DepthMetres > 100.0 && !(clear.MistMetres > 0.0), $"Yosemite's summer dawn: a pool {clear.DepthMetres:0} m deep, no mist");
        Assert.AreEqual("none", dry.Readings[Slider.Mist].Value);

        // The mist's water grows with how far below its dew point the air is, its extinction by Kunkel's fit, capped: no fog proper.
        Assert.AreEqual(0.4, Haze.VisibilityConstant / ValleyPools.MistExtinction(ValleyPools.MistWaterFor(0.4)) / 1000.0, 1e-9);
        Assert.IsTrue(Haze.VisibilityConstant / ValleyPools.MistExtinction(10.0) > 190.0, "never under about 200 m to see");
    }

    [TestMethod]
    public void APinnedMist_FillsTheScenesPool_AtItsVisibility()
    {
        SliderState pinned = At(Place.Yosemite, Season.Summer, 30.0, new SliderPins { Mist = 0.5 });
        PoolCell c = ValleyPools.Cell(pinned.Pools, pinned.Pools.FloorMetres, 1.0, double.MaxValue);
        Assert.IsTrue(pinned.Pools.WholePool, "a pinned mist keeps the night's whole pool");
        Assert.AreEqual(ValleyPools.PinnedMistShare * c.DepthMetres, c.MistMetres, 1e-6, "it fills its share of the pool");
        Assert.AreEqual(0.5, Haze.VisibilityConstant / ValleyPools.MistExtinction(c.MistWater) / 1000.0, 1e-6, "at the pinned visibility at the floor");
        StringAssert.StartsWith(pinned.Readings[Slider.Mist].Value, "500 m to see");
        Assert.AreEqual("none", At(Place.Fiordland, Season.Autumn, 2.0, new SliderPins { Mist = 0.0 }).Readings[Slider.Mist].Value, "0: none");
    }

    [TestMethod]
    public void EveryPlace_HasSourcedHumidityNormals()
    {
        foreach ((Place place, ClimateNormals normals) in ClimateTable.All)
        {
            Assert.AreEqual(0, normals.Validate().Count, $"{place}: {string.Join(" ", normals.Validate())}");
            Assert.IsNotNull(normals.VapourPressure, $"{place} has vapour pressures");
            Assert.IsFalse(string.IsNullOrWhiteSpace(normals.HumiditySource), $"{place} names its humidity's source");
            for (int month = 1; month <= 12; month++)
            {
                double dew = normals.DewPointOn(new DateOnly(2026, month, 15))!.Value;
                Assert.IsTrue(dew > normals.Lows[month - 1] - 15.0 && dew < normals.Highs[month - 1], $"{place} in month {month}: dew point {dew:0.0} °C");
            }
        }
        Assert.IsNull(Weather.Climate.For(Places.Custom(30.0)).DewPointOn(new DateOnly(2026, 6, 15)), "a custom latitude has none");
        // The dew point carried up by Hann's formula falls about 5 °C per kilometre near 0 °C.
        double drop = 0.0 - ValleyPools.DewPointAbove(0.0, 1000.0);
        Assert.IsTrue(drop is > 4.0 and < 6.0, $"{drop:0.0} °C per km");
        Assert.AreEqual(5.0, Humidity.DewPointCelsius(Humidity.SaturationVapourPressureHpa(5.0)), 1e-9, "Magnus inverted");
    }

    // ── The pools over a world ──────────────────────────────────────────────────────────────────────────────

    private static (ValleyPools Pools, LowAir Low, SliderState Sliders) Misty()
    {
        var cache = new WorldCache();
        TerrainWorld world = cache.World(Mountains, 42, TestWorlds.Grid, erode: false);
        SliderState sliders = LinkedSliders.Resolve(Mountains, new SliderPins
        {
            Location = Place.Zermatt, Date = new DateOnly(2026, 10, 15), SolarTime = new TimeOnly(7, 30), ValleyHaze = 6.0, Mist = 0.4,
        }, AtmosphereModel.Default, world.Footing);
        ValleyPools pools = ValleyPools.Build(cache.Pools(), sliders.Pools, sliders.BaseAltitudeMetres);
        AtmosphereModel air = sliders.Air(AtmosphereModel.Default, LowAir.TablesLayer(pools));
        return (pools, LowAir.For(pools, air, 42)!, sliders);
    }

    [TestMethod]
    public void TheDensity_IsGraded_Continuous_AndBounded()
    {
        (ValleyPools pools, LowAir low, _) = Misty();
        Assert.IsTrue(pools.Any && pools.Stats.MistShare > 0.0, pools.Stats.Describe());
        int graded = 0, columns = 0;
        for (int k = 0; k < 40; k++)
        {
            float x = -14000f + 700f * k, z = 9000f - 450f * k;
            PoolSample s = low.PoolAt(x, z, out bool any);
            if (!any || s.Depth < 60f)
                continue;
            columns++;
            // From the floor up past the top in 1 m steps: no jump larger than the top's steepest fall allows, nothing above the reach.
            float previous = low.Density(s, x, z, s.Floor).Haze, top = s.Depth + (float)(low.Settings.WispAmplitudeMetres + 8.0 * low.Settings.HazeSoftnessMetres);
            float steepest = Math.Max(s.HazeFloor, s.HazeTop) / (4f * (float)low.Settings.HazeSoftnessMetres);
            for (float u = 1f; u <= top + 50f; u += 1f)
            {
                (float haze, float mist) = low.Density(s, x, z, s.Floor + u);
                // The logistic's steepest slope, times what the wisps' own slope with height adds to it.
                Assert.IsTrue(Math.Abs(haze - previous) <= 4f * steepest + 1e-6f, $"a jump at {u} m: {previous} to {haze}");
                Assert.IsTrue(haze >= 0f && mist >= 0f && float.IsFinite(haze) && float.IsFinite(mist));
                if (u > top)
                    Assert.AreEqual(0f, haze, $"nothing above the reach ({u} m)");
                previous = haze;
            }
            graded += low.Density(s, x, z, s.Floor + 0.1f * s.Depth).Haze >= low.Density(s, x, z, s.Floor + 0.9f * s.Depth).Haze - 1e-6f ? 1 : 0;
        }
        Assert.IsTrue(columns >= 5 && graded >= 0.8 * columns, $"{graded} of {columns} columns densest low");

        // Nothing in the rock deeper than LowAir.FloorDepthMetres under a floor; the mist the same through its layer, under its top
        // (a deck at least 100 m deep, whose frayed top stands at least 60 m up, its logistic within 1% of the layer at 15 m).
        int misty = 0, deep = 0;
        for (int k = 0; k < 6400 && misty < 20; k++)
        {
            float x = -16000f + 400f * (k % 80), z = -16000f + 400f * (k / 80);
            PoolSample s = low.PoolAt(x, z, out bool any);
            if (!any || s.Depth <= 0f)
                continue;
            Assert.AreEqual((0f, 0f), low.Density(s, x, z, s.Floor - LowAir.FloorDepthMetres - 1f), "nothing in the rock");
            if (s.Mist < 100f)
                continue;
            deep++;
            // The layer's own value, which the top's logistic only ever lowers: reached deep inside it at both heights.
            float uniform = LowAir.MistExtinction(s.Water, (float)ValleyPools.MistWaterLimit);
            float lower = low.Density(s, x, z, s.Floor + 5f).Mist, higher = low.Density(s, x, z, s.Floor + 15f).Mist;
            Assert.IsTrue(lower <= uniform * 1.0001f && higher <= uniform * 1.0001f, "never denser than the layer's own value");
            if (lower >= 0.99f * uniform && higher >= 0.99f * uniform)
                misty++;
        }
        Assert.IsTrue(deep >= 3 && misty >= 0.8 * deep, $"{misty} of {deep} places with a deck 100 m deep uniform at 5 m and 15 m");

        // The noise is bounded and moves the tops by at most its amplitude.
        var random = new Random(5);
        for (int k = 0; k < 20000; k++)
        {
            float n3 = LowAir.Noise3(low.NoiseSeed, (float)(random.NextDouble() * 400 - 200), (float)(random.NextDouble() * 400 - 200), (float)(random.NextDouble() * 400 - 200));
            float n2 = LowAir.Noise2(low.NoiseSeed, (float)(random.NextDouble() * 400 - 200), (float)(random.NextDouble() * 400 - 200));
            Assert.IsTrue(Math.Abs(n3) <= 1f && Math.Abs(n2) <= 1.05f, $"{n3}, {n2}");
        }
    }

    /// <summary>
    /// Under a strong inversion a fog's top is flat and sharp, its edges frayed (the Sunnegga webcam, vista-plan §10): a deck at the cap
    /// deep over its ground takes none of the noise, the same deck where the ground rises to its top or a weak one all of it.
    /// </summary>
    [TestMethod]
    public void AStrongDecksTop_IsFlat_AndItsEdgesFray()
    {
        (_, LowAir low, _) = Misty();
        float cap = (float)ValleyPools.MistWaterLimit, fray = (float)low.Settings.FrayMetres;
        var deep = new PoolSample(1600f, 400f, 200f, 1f, 0.5f, cap, 3f * fray);
        Assert.AreEqual(0f, low.Fray(deep), "a strong deck over a valley's middle");
        Assert.AreEqual(1f, low.Fray(deep with { Cover = 0f }), "where the ground rises to its top");
        Assert.AreEqual(0.5f, low.Fray(deep with { Cover = 0.5f * fray }), 1e-6f, "half way to its edge");
        Assert.AreEqual(0.75f, low.Fray(deep with { Water = 0.25f * cap }), 1e-6f, "a weak deck");

        // Its top over the middle stands where the deck's depth says, wherever the noise is: the mist's logistic at its depth is a half.
        float extinction = LowAir.MistExtinction(cap, cap);
        int moved = 0;
        for (int k = 0; k < 50; k++)
        {
            float x = -9000f + 371f * k, z = 4000f - 283f * k;
            Assert.AreEqual(0.5f * extinction, low.Density(deep, x, z, deep.Floor + deep.Mist).Mist, 1e-4f * extinction, $"flat at ({x}, {z})");
            moved += Math.Abs(low.Density(deep with { Cover = 0f }, x, z, deep.Floor + deep.Mist).Mist - 0.5f * extinction) > 0.05f * extinction ? 1 : 0;
        }
        Assert.IsTrue(moved >= 25, $"the edge's top moves with the noise at {moved} of 50 places");
    }

    [TestMethod]
    public void TheCeiling_BoundsEveryPool()
    {
        (_, LowAir low, _) = Misty();
        var random = new Random(9);
        int under = 0;
        for (int k = 0; k < 20000; k++)
        {
            float x = (float)(random.NextDouble() * 60000 - 30000), z = (float)(random.NextDouble() * 60000 - 30000);
            float ceiling = low.CeilingAt(x, z);
            PoolSample s = low.PoolAt(x, z, out _);
            float altitude = ceiling + (float)(random.NextDouble() * 50.0);
            (float haze, float mist) = low.Density(s, x, z, altitude);
            Assert.AreEqual(0f, haze + mist, $"density above the ceiling at ({x}, {z}), {altitude} m");
            under += low.Density(s, x, z, ceiling - 200f).Haze > 0f ? 1 : 0;
        }
        Assert.IsTrue(under > 1000, $"the ceiling is not far above every pool ({under} places with haze 200 m under it)");
    }

    [TestMethod]
    public void TheTablesLayer_KeepsThePoolsHazeColumn()
    {
        (ValleyPools pools, _, _) = Misty();
        (double top, double extinction) = pools.AverageLayer()!.Value;
        double column = 0.0, highest = double.MinValue;
        foreach (PoolCell c in pools.Cells[0])
        {
            column += c.DepthMetres * 0.5 * (c.HazeFloor550 + c.HazeTop550);
            highest = Math.Max(highest, c.TopMetres);
        }
        column /= pools.Cells[0].Length;
        double mean = 0.0, weight = 0.0;
        foreach (PoolCell c in pools.Cells[0])
        {
            double m = c.DepthMetres * 0.5 * (c.HazeFloor550 + c.HazeTop550);
            mean += m * (c.FloorMetres + 0.5 * c.DepthMetres);
            weight += m;
        }
        mean /= weight;
        double depth = column / extinction;
        Assert.AreEqual(mean, top - depth / 2.0, 0.1 * depth, "a box about the pools' mean altitude");
        Assert.IsTrue(top < highest + 500.0, $"top {top:0} m, the highest pool's {highest:0} m");
    }

    [TestMethod]
    public void TheSunlightVolume_OnASyntheticRidge_ShadesWhatTheRidgeHides()
    {
        // A ridge 300 m high running east to west through a plain at 0 m; the sun from the south, 10° up.
        HeightField field = Field(257, 16.0, (_, j) => (float)Math.Max(0.0, 300.0 - Math.Abs(j - 128) * 16.0 * 1.0));
        double sinE = Math.Sin(10.0 * Math.PI / 180.0);
        bool Lit(double z, double height) => LowAir.TerrainLit(field, 0.0, 0.0, height, z, 0.0, -1.0, sinE);
        Assert.IsTrue(Lit(-600.0, 10.0), "south of the ridge, facing the sun: lit");
        Assert.IsFalse(Lit(600.0, 10.0), "north of it, low: in its shadow");
        // The shadow's top over a point north of the crest by d: the crest less d·tan 10°.
        double d = 900.0, line = 300.0 - d * Math.Tan(10.0 * Math.PI / 180.0);
        Assert.IsFalse(Lit(d, line - 20.0), "under the shadow's line");
        Assert.IsTrue(Lit(d, line + 20.0), "above it");
        Assert.IsTrue(Lit(-200.0, -50.0), "a point under the sunlit slope takes the ground above it");
    }

    // ── The slider and the option ───────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheMistSlider_PinsResetsAndChecks()
    {
        var pins = new SliderPins { Mist = 0.4 };
        Assert.IsTrue(pins.IsPinned(Slider.Mist));
        Assert.IsFalse(pins.Reset(Slider.Mist).IsPinned(Slider.Mist));
        Assert.AreEqual(Slider.Mist, SliderPins.Fields["mist"]);
        Assert.AreEqual(pins, VistaJson.ParseSliders("""{ "mist": 0.4 }"""));
        Assert.AreEqual(0, new SliderPins { Mist = 0.0 }.Validate().Count, "0: none");
        StringAssert.Contains(string.Join(" ", new SliderPins { Mist = 0.1 }.Validate()), "visibility in the mist must be in [0.2, 5]");
    }

    [TestMethod]
    public void TheOptions_PinTheMist_AndChooseThePools()
    {
        static PreviewRequest Parse(params string[] args)
        {
            Assert.IsTrue(PreviewCommandLine.TryParse(["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. args],
                out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
            return request!;
        }

        Assert.AreEqual("0.4", Parse("--mist", "0.4").SliderOverrides["mist"]);
        Assert.AreEqual("0", Parse("--mist", "off").SliderOverrides["mist"], "off: none");
        Assert.IsTrue(Parse().Pools, "pools by default");
        Assert.IsFalse(Parse("--pools", "flat").Pools, "M1.13a's flat layer");
        Assert.IsFalse(PreviewCommandLine.TryParse(["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", "--mist", "0.1"], out _, out IReadOnlyList<string> errors));
        StringAssert.Contains(string.Join(" ", errors), "--mist must be a number of km (or off) in [0.2, 5]");
        Assert.IsFalse(PreviewCommandLine.TryParse(["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", "--pools", "deep"], out _, out errors));
        StringAssert.Contains(string.Join(" ", errors), "--pools must be on or flat");
    }

    [TestMethod]
    public void TheStats_CountTheGroundUnderThePools()
    {
        (ValleyPools pools, _, _) = Misty();
        PoolStats s = pools.Stats;
        Assert.IsTrue(s.PooledShare is > 0.0 and < 1.0 && s.MistShare <= s.PooledShare, s.Describe());
        Assert.IsTrue(s.LowestTopMetres <= s.HighestTopMetres && s.DeepestMistMetres <= s.DeepestMetres);
        Assert.IsTrue(s.ThinnestMistKm is > 0.19 and < 5.0, $"{s.ThinnestMistKm:0.000} km: never fog proper");
        StringAssert.Contains(s.Describe(), "pools over");
        Assert.AreEqual(PoolStats.None, ValleyPools.None.Stats);
    }
}
