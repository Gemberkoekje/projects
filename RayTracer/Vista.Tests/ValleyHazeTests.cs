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
/// The valley haze (vista-plan §10 M1.13a verify: "CI: the profile (denser below the lid, the lid's sharpness, continuity), the chain
/// (a cold autumn dawn gives a layer, a warm afternoon none)"): the layer in the atmosphere, its exact means along a march's steps,
/// M1.13's air kept bit for bit without it, and the weather chain that sets it.
/// </summary>
[TestClass]
public sealed class ValleyHazeTests
{
    private const double Lid = 2000.0;

    private static readonly AtmosphereModel Layer = AtmosphereModel.Default with { ValleyHazeTopMetres = Lid, ValleyHazeExtinction550 = 1e-4 };

    private static readonly Recipe Mountains = new() { SetPieces = [SetPiece.SnowCappedMountains] };

    /// <summary>The seed-42 world's footing at M1.13: its lowest valleys 651 m above its height 0, its crest 1134 m above them.</summary>
    private static readonly WorldFooting World = new(WorldFooting.DefaultPercentile, 651.0, 392.0) { CrestMetres = 1785.0 };

    private static SliderState At(Place place, string date, string solarTime, SliderPins? more = null) =>
        LinkedSliders.Resolve(Mountains, (more ?? SliderPins.None) with
        {
            Location = place,
            Date = DateOnly.Parse(date, CultureInfo.InvariantCulture),
            SolarTime = TimeOnly.Parse(solarTime, CultureInfo.InvariantCulture),
        }, AtmosphereModel.Default, World);

    [TestMethod]
    public void TheLayer_IsUniformUnderItsLid_HalfAtIt_AndGoneAbove()
    {
        double density = Layer.ValleyHazeDensity;
        Assert.AreEqual(1e-4 * Layer.AerosolScaleHeightMetres / Layer.AerosolOpticalDepth550, density, 1e-15);
        Assert.AreEqual(density, Layer.ValleyHaze(Lid - 1000.0), 1e-12 * density, "uniform under the lid");
        Assert.AreEqual(0.5 * density, Layer.ValleyHaze(Lid), 1e-15, "half at the lid");
        Assert.IsTrue(Layer.ValleyHaze(Lid + 1000.0) < 1e-12 * density, "gone above it");
        // Its extinction under the lid is the one asked for: the haze's coefficient times the layer's density in the haze's unit.
        Assert.AreEqual(1e-4, Layer.CoefficientsAt(550.0).AerosolExtinction * Layer.ValleyHaze(Lid - 1000.0), 1e-15);
        // On top of the haze's own profile.
        Assert.AreEqual(Math.Exp(-1500.0 / 1200.0) + Layer.ValleyHaze(1500.0), Layer.AerosolDensity(1500.0), 1e-15);
    }

    [TestMethod]
    public void TheLid_IsSoftButDistinct_AndContinuous()
    {
        double density = Layer.ValleyHazeDensity, w = Layer.ValleyHazeSoftnessMetres;
        // From 90% to 10% of the layer over 2·ln 9 softnesses: 132 m at the default 30 m.
        Assert.AreEqual(0.9, Layer.ValleyHaze(Lid - w * Math.Log(9.0)) / density, 1e-12);
        Assert.AreEqual(0.1, Layer.ValleyHaze(Lid + w * Math.Log(9.0)) / density, 1e-12);
        Assert.AreEqual(131.8, 2.0 * w * Math.Log(9.0), 0.1);
        // No step anywhere: half a metre never moves it by more than the logistic's steepest slope, density/(4w), allows.
        double steepest = 0.0;
        for (double h = Lid - 500.0; h < Lid + 500.0; h += 0.5)
            steepest = Math.Max(steepest, Math.Abs(Layer.AerosolDensity(h + 0.5) - Layer.AerosolDensity(h)) / 0.5);
        double haze = 1.0 / 1200.0; // the haze's own profile falls at most this fast per metre, at sea level
        Assert.IsTrue(steepest <= density / (4.0 * w) + haze, $"steepest {steepest:E3} per m");
        Assert.IsTrue(steepest > 0.9 * density / (4.0 * w), "and the lid is that steep");
    }

    [TestMethod]
    public void WithoutALayer_TheHazeIsM1_13s_BitForBit()
    {
        AtmosphereModel m = AtmosphereModel.Default;
        Assert.AreEqual(1.0, m.HazeScale);
        Assert.AreEqual(0.0, m.ValleyHazeDensity);
        foreach (double h in new[] { 0.0, 911.7, 1608.0, 2400.5, 9000.0 })
        {
            Assert.AreEqual(Math.Exp(-h / m.AerosolScaleHeightMetres), m.AerosolDensity(h), 0.0, $"{h} m");
            Assert.AreEqual(m.AerosolDensity(h), m.AerosolStepDensity(h - 37.0, h, h + 41.0), 0.0, "a step's haze is its middle's");
        }
        foreach (double nm in new[] { 400.0, 550.0, 655.0 })
            Assert.AreEqual(m.AerosolOpticalDepth550 * Math.Pow(nm / 550.0, -m.AngstromExponent) / m.AerosolScaleHeightMetres,
                m.CoefficientsAt(nm).AerosolExtinction, 0.0);
        double top = m.TopAltitudeMetres;
        Assert.AreEqual(1200.0 * (Math.Exp(-1608.0 / 1200.0) - Math.Exp(-top / 1200.0)), m.ZenithColumns(1608.0).Aerosol, 0.0);
    }

    [TestMethod]
    public void TheStepMean_IsTheLayersExactIntegral()
    {
        foreach ((double a, double b) in new[] { (1500.0, 2500.0), (1990.0, 2010.0), (1000.0, 1900.0), (2100.0, 4000.0), (1999.0, 1999.0005) })
        {
            // Simpson's rule on a fine grid.
            const int n = 200_000;
            double dh = (b - a) / n, sum = Layer.ValleyHaze(a) + Layer.ValleyHaze(b);
            for (int i = 1; i < n; i++)
                sum += (i % 2 == 1 ? 4.0 : 2.0) * Layer.ValleyHaze(a + i * dh);
            double integral = sum * dh / 3.0;
            Assert.AreEqual(integral, Layer.ValleyHazeMean(a, b) * (b - a), 1e-9 * Math.Max(Math.Abs(integral), 1e-12), $"{a}–{b}");
            Assert.AreEqual(Layer.ValleyHazeMean(a, b), Layer.ValleyHazeMean(b, a), 1e-12 * Layer.ValleyHazeDensity, "either order");
        }
        Assert.AreEqual(Layer.ValleyHaze(1999.0), Layer.ValleyHazeMean(1999.0, 1999.0), 1e-15, "no length: the point's density");
        Assert.AreEqual(Layer.ValleyHazeDensity, Layer.ValleyHazeMean(0.0, 1000.0), 1e-12, "all under the lid");
    }

    [TestMethod]
    public void AVerticalColumn_GetsTheLayersExactColumn()
    {
        foreach (double h in new[] { 1608.0, 1950.0, 2030.0, 2600.0 })
        {
            double layer = AtmosphereLuts.ColumnsAlong(Layer, h, 1.0, AtmosphereLuts.ColumnSteps).Aerosol
                - AtmosphereLuts.ColumnsAlong(AtmosphereModel.Default, h, 1.0, AtmosphereLuts.ColumnSteps).Aerosol;
            double exact = Layer.ValleyHazeColumn(h, Layer.TopAltitudeMetres);
            Assert.AreEqual(exact, layer, 1e-7 * Layer.ValleyHazeDensity * 400.0 + 1e-9, $"{h} m: the march's steps of 98 m, the layer's 30 m lid");
            Assert.AreEqual(exact, Layer.ZenithColumns(h).Aerosol - AtmosphereModel.Default.ZenithColumns(h).Aerosol, 1e-12);
        }
        Assert.AreEqual(Layer.ValleyHazeDensity * (Lid - 1608.0), Layer.ValleyHazeColumn(1608.0, Layer.TopAltitudeMetres), 0.5,
            "about its density times the depth to the lid");
    }

    [TestMethod]
    public void ASegmentThroughTheLid_KeepsTheLayersOpticalDepth_InTheMarchsFewSteps()
    {
        // Segments from 1.7 km above the ground sphere at 1608 m, 20 km long, ending at heights that sweep across the lid (2058 m,
        // 450 m up): eight steps, against a fine integral of the same layer. A sample per step would jump wherever the lid crosses a
        // step's middle, a band in the picture; the step means keep the layer's share of every segment's optical depth (the march
        // with it less the march without it) within 10⁻⁴ of the layer's own, about 1.6 here, while a sample's misses by up to 0.03:
        // a 3% jump in the light from beyond, which bands a smooth slope.
        AtmosphereModel layer = Layer with { ValleyHazeTopMetres = 2058.0, ValleyHazeExtinction550 = 5e-4 };
        AtmosphereFrame frame = AtmosphereFrame.For(layer, 1608.0, new System.Numerics.Vector3(0.6f, 0.5f, 0.62f), 1700.0, 0.0, 2400.0);
        AtmosphereFrame plain = frame with { Model = AtmosphereModel.Default };
        AtmosphereCoefficients k = layer.CoefficientsAt(550.0);
        double worst = 0.0, worstSampled = 0.0;
        for (double end = 300.0; end <= 600.0; end += 2.5)
        {
            const double length = 20_000.0;
            double mu = (end - 1700.0) / length;
            var segment = new AerialPerspective.Segment(1700.0, mu, 0.5, 0.3, length);
            double With(AtmosphereFrame f) => -Math.Log(AerialPerspective.Integrate(AtmosphereLutsTests.Columns, [], f, k, 0.0, segment).Transmittance);
            double marched = With(frame) - With(plain);
            const int fine = 40_000;
            double exact = 0.0;
            for (int i = 0; i < fine; i++)
                exact += 5e-4 / layer.ValleyHazeDensity * layer.ValleyHaze(frame.GroundAltitudeMetres
                    + Math.Max(AtmosphereLuts.AltitudeAlong(frame.GroundRadius, 1700.0, mu, (i + 0.5) * length / fine), 0.0)) * length / fine;
            double sampled = 0.0;
            int steps = AerialPerspective.StepsFor(length);
            for (int s = 0; s < steps; s++)
            {
                (double t, double dt) = AtmosphereLuts.Segment(length, s, steps, denseAtEnd: true);
                sampled += 5e-4 / layer.ValleyHazeDensity * layer.ValleyHaze(frame.GroundAltitudeMetres
                    + Math.Max(AtmosphereLuts.AltitudeAlong(frame.GroundRadius, 1700.0, mu, t), 0.0)) * dt;
            }
            worst = Math.Max(worst, Math.Abs(marched - exact));
            worstSampled = Math.Max(worstSampled, Math.Abs(sampled - exact));
        }
        Assert.IsTrue(worst < 1e-4, $"worst error of the layer's optical depth {worst:E2}");
        Assert.IsTrue(worstSampled > 0.02, $"a sample per step would miss by {worstSampled:0.000}");
    }

    [TestMethod]
    public void InCleanAir_TheLayerKeepsItsHaze()
    {
        // A pinned visibility beyond clean air's leaves no haze of the air's own; the valley haze still has its coefficients.
        AtmosphereModel clean = Layer with { AerosolOpticalDepth550 = 0.0 };
        Assert.AreEqual(0.0, clean.HazeScale);
        Assert.AreEqual(AtmosphereModel.ClearDayOpticalDepth550, clean.HazeUnitOpticalDepth);
        Assert.AreEqual(1e-4, clean.CoefficientsAt(550.0).AerosolExtinction * clean.AerosolDensity(1000.0), 1e-15);
        Assert.AreEqual(0.0, clean.AerosolDensity(5000.0), 1e-20);
        Assert.AreEqual(0.0, Haze.Clean(Layer).CoefficientsAt(550.0).AerosolExtinction, "no haze at all");
        Assert.AreEqual(0, clean.Validate().Count);
    }

    [TestMethod]
    [DataRow(-1e-4, 30.0, 2000.0, "must not be negative")]
    [DataRow(1e-4, 0.0, 2000.0, "ValleyHazeSoftnessMetres must be positive")]
    [DataRow(1e-4, 30.0, -10.0, "between sea level and the top")]
    public void ABadLayer_IsRefused(double extinction, double softness, double top, string message)
    {
        AtmosphereModel bad = AtmosphereModel.Default with
        {
            ValleyHazeExtinction550 = extinction, ValleyHazeSoftnessMetres = softness, ValleyHazeTopMetres = top,
        };
        StringAssert.Contains(string.Join(" ", bad.Validate()), message);
    }

    // ── The weather chain ──────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AColdAutumnDawn_GivesALayer()
    {
        SliderState dawn = LinkedSliders.Resolve(Mountains, new SliderPins
        {
            Location = Place.Zermatt, Date = new DateOnly(2026, 10, 15), Time = TimeOfDay.Dawn,
        }, AtmosphereModel.Default, World);
        ValleyHazeLayer layer = dawn.ValleyHaze;
        Assert.IsTrue(layer.Any, "a layer");
        Assert.IsTrue(layer.PoolShare > 0.95, $"the whole night's pool before sunrise ({layer.PoolShare:P0})");
        Assert.IsTrue(layer.DepthMetres is > 300.0 and < 600.0, $"{layer.DepthMetres:0} m deep");
        Assert.AreEqual(dawn.ValleyFloorMetres + layer.DepthMetres, layer.TopMetres, 1e-9, "its lid stands on the valley floor's pool");
        Assert.IsTrue(layer.VisibilityKm < 0.5 * dawn.VisibilityKm, $"{layer.VisibilityKm:0.#} km under the lid, {dawn.VisibilityKm:0} km in the valley's own haze");
        Assert.AreEqual($"{layer.VisibilityKm:0.#} km under a lid at {layer.TopMetres:0} m".Replace(',', '.'), dawn.Readings[Slider.ValleyHaze].Value);
        StringAssert.StartsWith(dawn.Readings[Slider.ValleyHaze].From, "auto: a cold pool");
        // The render's air carries it.
        AtmosphereModel air = dawn.Air(AtmosphereModel.Default);
        Assert.AreEqual(layer.TopMetres, air.ValleyHazeTopMetres);
        Assert.AreEqual(layer.Extinction550, air.ValleyHazeExtinction550);
        Assert.AreEqual(dawn.ValleyHaze, LinkedSliders.Resolve(Mountains, dawn.Pins, AtmosphereModel.Default, World).ValleyHaze, "pure");
    }

    [TestMethod]
    public void AWarmAfternoon_GivesNone()
    {
        foreach (string time in new[] { "12:00", "15:00" })
        {
            SliderState state = At(Place.Zermatt, "2026-07-15", time);
            Assert.IsFalse(state.ValleyHaze.Any, $"{time}: {state.Readings[Slider.ValleyHaze].From}");
            Assert.AreEqual("none", state.Readings[Slider.ValleyHaze].Value);
            Assert.AreEqual(0.0, state.Air(AtmosphereModel.Default).ValleyHazeExtinction550);
        }
    }

    [TestMethod]
    public void TheMorningSun_BreaksThePoolUp_AndTheEveningBuildsItAgain()
    {
        double before = 1.0;
        foreach (string time in new[] { "07:00", "08:00", "09:00", "10:00" })
        {
            double share = At(Place.Zermatt, "2026-10-15", time).ValleyHaze is { Any: true } layer ? layer.PoolShare : 0.0;
            Assert.IsTrue(share < before, $"{time}: {share:P0} after {before:P0}");
            before = share;
        }
        Assert.IsFalse(At(Place.Zermatt, "2026-10-15", "12:00").ValleyHaze.Any, "gone by noon on a snow-free October floor");
        // Some 3.5 to 5 hours after sunrise, as deep valleys' inversions break (Whiteman 1982).
        SliderState morning = At(Place.Zermatt, "2026-04-15", "06:00");
        double sunrise = morning.Almanac.Daylight.SunriseHours;
        double gone = Enumerable.Range(0, 96).Select(i => sunrise + i * 0.1)
            .First(h => !At(Place.Zermatt, "2026-04-15", TimeOnly.FromTimeSpan(TimeSpan.FromHours(h)).ToString("HH:mm:ss", CultureInfo.InvariantCulture)).ValleyHaze.Any);
        Assert.IsTrue(gone - sunrise is > 3.0 and < 5.5, $"gone {gone - sunrise:0.0} h after sunrise");
        // After the day's high it forms again as the air cools: some by sunset, the whole of it by the next dawn.
        double dusk = At(Place.Zermatt, "2026-10-15", "19:00").ValleyHaze.PoolShare;
        double night = At(Place.Zermatt, "2026-10-15", "03:00").ValleyHaze.PoolShare;
        Assert.IsTrue(dusk is > 0.0 and < 0.6 && night > dusk, $"dusk {dusk:P0}, night {night:P0}");
    }

    [TestMethod]
    public void ASnowyFloor_KeepsThePoolThroughAWinterDay()
    {
        // Zermatt's January floor lies under the season's snow, which turns most of the sun away: the pool outlasts noon.
        SliderState snowy = At(Place.Zermatt, "2027-01-15", "12:00");
        Assert.IsTrue(snowy.SnowlineMetres < snowy.ValleyFloorMetres, "the floor is snowy");
        Assert.IsTrue(snowy.ValleyHaze.Any && snowy.ValleyHaze.PoolShare > 0.3, $"{snowy.ValleyHaze.PoolShare:P0} at noon");
        // The same day without snow: gone by noon.
        SliderState bare = At(Place.Zermatt, "2027-01-15", "12:00", new SliderPins { Snowline = SliderPins.MaxSnowlineMetres });
        Assert.IsTrue(!bare.ValleyHaze.Any || bare.ValleyHaze.PoolShare < snowy.ValleyHaze.PoolShare - 0.3, $"{bare.ValleyHaze.PoolShare:P0} without snow");
    }

    [TestMethod]
    public void ThePool_IsShallowOnAWindyCoast_AndDeepInADryValley()
    {
        double Depth(Place place, string date) => LinkedSliders.Resolve(Mountains,
            new SliderPins { Location = place, Date = DateOnly.Parse(date, CultureInfo.InvariantCulture), Time = TimeOfDay.Dawn },
            AtmosphereModel.Default, World).ValleyHaze.DawnDepthMetres;
        double lofoten = Depth(Place.Lofoten, "2026-07-15"), zermatt = Depth(Place.Zermatt, "2026-07-15"), yosemite = Depth(Place.Yosemite, "2026-07-15");
        Assert.IsTrue(lofoten < 250.0 && zermatt is > 350.0 and < 600.0 && yosemite > zermatt, $"Lofoten {lofoten:0}, Zermatt {zermatt:0}, Yosemite {yosemite:0} m");
        // Never more than half the world's relief: a pool fills its valleys, not the peaks above them.
        Assert.IsTrue(yosemite <= ValleyHaze.ReliefShare * World.ReliefMetres + 1e-9, $"{yosemite:0} m in {World.ReliefMetres:0} m of relief");
        var low = new WorldFooting(WorldFooting.DefaultPercentile, 651.0, 392.0) { CrestMetres = 851.0 };
        SliderState hills = LinkedSliders.Resolve(Mountains, new SliderPins { Time = TimeOfDay.Dawn }, AtmosphereModel.Default, low);
        Assert.AreEqual(100.0, hills.ValleyHaze.DawnDepthMetres, 1e-9, "half of 200 m of relief");
    }

    [TestMethod]
    public void APinnedValleyHaze_StandsAtAnyHour_AndOffIsNone()
    {
        SliderState pinned = At(Place.Zermatt, "2026-07-15", "13:00", new SliderPins { ValleyHaze = 8.0 });
        Assert.IsTrue(pinned.ValleyHaze.Any);
        Assert.AreEqual(8.0, pinned.ValleyHaze.VisibilityKm, 1e-9, "the visibility under the lid is the pin");
        Assert.AreEqual(1.0, pinned.ValleyHaze.PoolShare);
        Assert.AreEqual(pinned.ValleyHaze.DawnDepthMetres, pinned.ValleyHaze.DepthMetres, "the night's whole pool");
        Assert.IsTrue(pinned.Readings[Slider.ValleyHaze].Pinned);
        Assert.AreEqual(8.0, pinned.Readings[Slider.ValleyHaze].Number!.Value, 1e-9);
        SliderState off = At(Place.Zermatt, "2026-10-15", "06:00", new SliderPins { ValleyHaze = SliderPins.MaxVisibilityKm });
        Assert.IsFalse(off.ValleyHaze.Any, "a visibility no shorter than the valley's own adds nothing");
        Assert.IsTrue(At(Place.Zermatt, "2026-10-15", "06:00").ValleyHaze.Any, "while the auto dawn has one");
    }

    [TestMethod]
    public void AWarmPin_ThinsTheDawnLayer_ThroughTheHumidity()
    {
        SliderState auto = At(Place.Zermatt, "2026-10-15", "06:00");
        SliderState warm = At(Place.Zermatt, "2026-10-15", "06:00", new SliderPins { Temperature = 18.0 });
        Assert.IsTrue(warm.HumidityPercent < auto.HumidityPercent);
        Assert.IsTrue(warm.ValleyHaze.Extinction550 < auto.ValleyHaze.Extinction550, "drier air under the lid holds less haze");
        Assert.AreEqual(auto.ValleyHaze.DepthMetres, warm.ValleyHaze.DepthMetres, "the pool is the night's, whatever the hour's air");
    }

    [TestMethod]
    public void TheSlider_PinsResetsAndChecks()
    {
        var pins = new SliderPins { ValleyHaze = 12.0 };
        Assert.IsTrue(pins.IsPinned(Slider.ValleyHaze));
        CollectionAssert.AreEqual(new[] { Slider.ValleyHaze }, pins.Pinned.ToArray());
        Assert.IsFalse(pins.Reset(Slider.ValleyHaze).IsPinned(Slider.ValleyHaze));
        Assert.AreEqual(Slider.ValleyHaze, SliderPins.Fields["valleyHaze"]);
        Assert.AreEqual(pins, VistaJson.ParseSliders("""{ "valleyHaze": 12 }"""));
        StringAssert.Contains(string.Join(" ", new SliderPins { ValleyHaze = 0.5 }.Validate()), "visibility under the valley haze must be in [1, 500]");
    }

    [TestMethod]
    public void TheOption_PinsTheSlider()
    {
        static PreviewRequest Parse(params string[] args)
        {
            Assert.IsTrue(PreviewCommandLine.TryParse(["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. args],
                out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
            return request!;
        }

        Assert.AreEqual("6", Parse("--valley-haze", "6").SliderOverrides["valleyHaze"]);
        Assert.AreEqual("500", Parse("--valley-haze", "off").SliderOverrides["valleyHaze"], "off: no shorter than the valley's own");
        Assert.IsFalse(PreviewCommandLine.TryParse(["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", "--valley-haze", "0.5"],
            out _, out IReadOnlyList<string> errors));
        StringAssert.Contains(string.Join(" ", errors), "--valley-haze must be a number of km (or off) in [1, 500]");
    }

    [TestMethod]
    public void TheFooting_MeasuresTheCrest()
    {
        const int size = 65;
        int count = size * size;
        var heights = new float[count];
        for (int k = 0; k < count; k++)
            heights[k] = (k * 2654435761u % (uint)count) * 0.5f;
        WorldFooting footing = WorldFooting.Of(new HeightField(size, 8.0, heights));
        Assert.AreEqual(Math.Round(0.95 * (count - 1)) * 0.5, footing.CrestMetres);
        Assert.AreEqual(footing.CrestMetres - footing.HeightMetres, footing.ReliefMetres);
    }
}
