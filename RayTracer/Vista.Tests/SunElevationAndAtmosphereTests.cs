using System.Text.Json.Nodes;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// M1.3's plumbing outside the LUTs: the recipe's sun elevation, the CLI options, the scene's base altitude, the
/// schema-4 sidecar (atmosphere, exposure, transmittance to the camera) and the sheet labels.
/// </summary>
[TestClass]
public sealed class SunElevationAndAtmosphereTests
{
    private string _dir = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    // ── Sun elevation ────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(46.02, -6.0, false)]
    [DataRow(46.02, -2.0, true)]
    [DataRow(46.02, 10.0, false)]
    [DataRow(-51.08, 5.0, true)]
    [DataRow(67.93, 0.5, false)]
    public void HoursAtElevation_PutTheSunThere(double latitude, double elevation, bool morning)
    {
        var date = new DateOnly(2026, 3, 20);
        (double hours, bool reachable) = SolarPosition.HoursAtElevation(latitude, date, elevation, morning);
        Assert.IsTrue(reachable);
        Assert.AreEqual(morning, hours < 12.0);
        Assert.AreEqual(elevation, SolarPosition.At(latitude, date, hours).TrueElevationDegrees, 1e-6);
    }

    [TestMethod]
    public void HoursAtElevation_OutOfReach_IsNoonOrMidnight()
    {
        var date = new DateOnly(2026, 12, 21);
        (double high, bool reachableHigh) = SolarPosition.HoursAtElevation(46.02, date, 60.0, morning: false);
        Assert.IsFalse(reachableHigh);
        Assert.AreEqual(12.0, high, 1e-9);
        (double low, bool reachableLow) = SolarPosition.HoursAtElevation(46.02, date, -80.0, morning: false);
        Assert.IsFalse(reachableLow);
        Assert.AreEqual(24.0, low, 1e-9);
    }

    [TestMethod]
    public void SunElevation_ReplacesThePreset_RisingOnlyAtDawn()
    {
        var recipe = new Recipe { Location = Place.Zermatt, Date = new DateOnly(2026, 7, 15), SunElevation = -2.0, Time = TimeOfDay.GoldenHour };
        Almanac evening = Almanac.For(recipe);
        Assert.AreEqual(-2.0, evening.Sun.TrueElevationDegrees, 1e-6);
        Assert.IsTrue(evening.SolarHours > 12.0);
        Assert.AreEqual("sunElevation -2°, setting", evening.SolarTimeFrom);

        Almanac morning = Almanac.For(recipe with { Time = TimeOfDay.Dawn });
        Assert.IsTrue(morning.SolarHours < 12.0);
        Assert.AreEqual("sunElevation -2°, rising", morning.SolarTimeFrom);
    }

    [TestMethod]
    public void SunElevation_TheSunNeverReaches_IsAHint()
    {
        Almanac almanac = Almanac.For(new Recipe { Location = Place.Zermatt, Date = new DateOnly(2026, 12, 21), SunElevation = 40.0 });
        Assert.AreEqual(12.0, almanac.SolarHours, 1e-9);
        Assert.AreEqual(1, almanac.Hints.Count);
        StringAssert.Contains(almanac.Hints[0], "never stands at 40°");
    }

    [TestMethod]
    public void Recipe_SunElevation_ParsesAndValidates()
    {
        Recipe parsed = VistaJson.ParseRecipe("""{ "schema": 3, "sunElevation": -4.5 }""");
        Assert.AreEqual(-4.5, parsed.SunElevation);
        Assert.IsFalse(parsed.ContentEquals(parsed with { SunElevation = -4.0 }));
        Assert.ThrowsExactly<InvalidDataException>(() => VistaJson.ParseRecipe("""{ "sunElevation": 95 }"""));
        Assert.ThrowsExactly<InvalidDataException>(() => VistaJson.ParseRecipe("""{ "sunElevation": 5, "solarTime": "17:00" }"""));
    }

    // ── Command line ─────────────────────────────────────────────────────────────────────────────────

    private static PreviewRequest Parse(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" ", errors));
        return request!;
    }

    [TestMethod]
    public void SunElevationOption_IsARecipeOverride_ThatClearsTheSolarTime()
    {
        PreviewRequest r = Parse("--sun-elevation", "-3.5");
        Assert.AreEqual("-3.5", r.RecipeOverrides["sunElevation"]);
        Assert.AreEqual("null", r.RecipeOverrides["solarTime"]);
        Assert.AreEqual("null", Parse("--time", "17:30").RecipeOverrides["sunElevation"], "a solar time clears a sun elevation");
        Assert.IsFalse(PreviewCommandLine.TryParse(["--preview", "--recipe", "r", "--seed", "1", "--out", "p.png", "--time", "17:30", "--sun-elevation", "2"], out _, out _));
        Assert.IsFalse(PreviewCommandLine.TryParse(["--preview", "--recipe", "r", "--seed", "1", "--out", "p.png", "--sun-elevation", "high"], out _, out _));
    }

    [TestMethod]
    public void AtmosphereOption_PicksTheMode()
    {
        Assert.AreEqual(AtmosphereMode.Luts, Parse().Atmosphere);
        Assert.AreEqual(AtmosphereMode.Reference, Parse("--atmosphere", "reference").Atmosphere);
        Assert.IsFalse(PreviewCommandLine.TryParse(["--preview", "--recipe", "r", "--seed", "1", "--out", "p.png", "--atmosphere", "none"], out _, out IReadOnlyList<string> errors));
        StringAssert.Contains(errors[0], "luts or reference");
    }

    [TestMethod]
    public void SheetAxis_VariesTheAtmosphereMode()
    {
        Assert.IsTrue(SheetAxis.TryParse("atmosphere=luts,reference", out SheetAxis? axis, out _));
        PreviewRequest r = Parse();
        Assert.AreEqual(AtmosphereMode.Reference, axis!.Apply(r, 1).Atmosphere);
        Assert.IsFalse(SheetAxis.TryParse("atmosphere=luts,fog", out _, out string? error));
        StringAssert.Contains(error, "fog");
    }

    // ── Pipeline and sidecar ─────────────────────────────────────────────────────────────────────────

    private PreviewRequest Request(string recipeJson, bool dryRun = false)
    {
        File.WriteAllText(Path.Combine(_dir, "r.json"), recipeJson);
        return new PreviewRequest { RecipePath = Path.Combine(_dir, "r.json"), Seed = 7, OutPath = Path.Combine(_dir, "p.png"), Width = 32, Height = 18, SamplesPerPixel = 4, DryRun = dryRun };
    }

    [TestMethod]
    public void Scene_SitsAtThePlacesValleyAltitude()
    {
        var renderer = new FakeRenderer();
        PreviewPipeline.Run(Request("""{ "setPieces": ["alpineRange"], "location": "yellowstone" }"""), renderer);
        Assert.AreEqual(Places.Get(Place.Yellowstone).ValleyAltitudeMetres, renderer.Scene!.GroundAltitudeMetres);
        Assert.AreEqual(AtmosphereModel.Default, renderer.Scene.Air);
        PreviewPipeline.Run(Request("""{ "latitude": 10 }"""), renderer);
        Assert.AreEqual(0.0, renderer.Scene!.GroundAltitudeMetres, "a custom latitude is at sea level until M1.4");
    }

    [TestMethod]
    public void Render_MetersTheWholeFrame_AndRecordsIt()
    {
        var renderer = new FakeRenderer();
        PreviewRequest request = Request("""{ "setPieces": ["alpineRange"] }""") with { Loupe = new PixelWindow(4, 2, 8, 8) };
        Sidecar sidecar = PreviewPipeline.Run(request, renderer);

        RenderSettings meter = renderer.Meters.Single();
        Assert.AreEqual((AutoExposure.MeterWidth, 180), (meter.Width, meter.Height));
        Assert.IsTrue(meter.Window.IsEmpty);
        ExposureInfo exposure = sidecar.Exposure!;
        Assert.AreEqual((320, 180, AutoExposure.MeterSamplesPerPixel), (exposure.MeterWidth, exposure.MeterHeight, exposure.MeterSamplesPerPixel));
        Assert.AreEqual(AtmosphereMode.Luts, sidecar.Atmosphere.Mode);
        Assert.AreEqual(Sidecar.CurrentSchema, sidecar.Schema);
        Assert.AreEqual(4, Sidecar.CurrentSchema);
        Assert.AreEqual(ImageInfo.RadianceUnits, sidecar.Image.Units);
        Assert.IsTrue(sidecar.TimingsSeconds.ContainsKey("exposureMeter"));
    }

    [TestMethod]
    public void DryRun_HasAnAtmosphereButNoExposure()
    {
        Sidecar sidecar = PreviewPipeline.Run(Request("""{ "setPieces": ["alpineRange"] }""", dryRun: true), renderer: null);
        Assert.IsNull(sidecar.Exposure);
        Assert.AreEqual(1608.0, sidecar.Atmosphere.GroundAltitudeMetres, "Zermatt's valley floor");
        Assert.AreEqual(0.097, sidecar.Atmosphere.ZenithFromSeaLevel.Rayleigh550, 0.001);
        Assert.IsTrue(sidecar.Atmosphere.ZenithFromGround.Aerosol550 < 0.5 * sidecar.Atmosphere.ZenithFromSeaLevel.Aerosol550, "most haze lies below the valley");
    }

    [TestMethod]
    public void SunInfo_RecordsTheReddeningAndTheEarthsShadow()
    {
        Sidecar day = PreviewPipeline.Run(Request("""{ "time": "day" }""", dryRun: true), renderer: null);
        Sidecar low = PreviewPipeline.Run(Request("""{ "sunElevation": 1 }""", dryRun: true), renderer: null);
        Sidecar dusk = PreviewPipeline.Run(Request("""{ "sunElevation": -4 }""", dryRun: true), renderer: null);
        Assert.IsTrue(day.Sun.TransmittanceToCamera.At650Nm > 0.8 && day.Sun.TransmittanceToCamera.At450Nm > 0.6);
        Assert.IsTrue(low.Sun.TransmittanceToCamera.At650Nm > 3.0 * low.Sun.TransmittanceToCamera.At450Nm, "a low sun is red");
        Assert.IsTrue(dusk.Sun.InEarthShadowAtCamera);
        Assert.AreEqual(0.0, dusk.Sun.TransmittanceToCamera.At550Nm);
    }

    // ── Labels ───────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Labels_NameTheSunElevationAndTheAtmosphere()
    {
        JsonNode? Sidecar(string recipe, string mode) => JsonNode.Parse($$"""{ "recipe": {{recipe}}, "atmosphere": { "mode": "{{mode}}" } }""");
        IReadOnlyDictionary<string, string> setting = SheetLabels.Identity(Sidecar("""{ "time": "goldenHour", "sunElevation": -2 }""", "luts"));
        Assert.IsFalse(setting.ContainsKey("recipe.time"), "the elevation replaced the preset");
        Assert.AreEqual("-2", setting["recipe.sunElevation"]);
        IReadOnlyDictionary<string, string> rising = SheetLabels.Identity(Sidecar("""{ "time": "dawn", "sunElevation": -2 }""", "reference"));
        Assert.AreEqual("dawn", rising["recipe.time"], "dawn says the sun is rising");
        Assert.AreEqual("reference", rising["atmosphere"]);
        IReadOnlyList<string> labels = SheetLabels.Differences([setting, rising]);
        StringAssert.Contains(labels[1], "atmosphere=reference");
        StringAssert.Contains(SheetLabels.Title([setting, setting]), "sun at -2°");
    }
}
