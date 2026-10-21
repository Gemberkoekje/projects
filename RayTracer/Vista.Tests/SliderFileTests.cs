using System.Text.Json;
using System.Text.Json.Nodes;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Sliders;

namespace Vista.Tests;

/// <summary>
/// The slider file and the slider options end to end (vista-plan §10 M1.11): the CLI pins sliders, a preview reads its slider file
/// (or the recipe's own pins, never both), the sidecar records every slider, sheets vary sliders and label them, and a watch
/// renders again when the slider file changes.
/// </summary>
[TestClass]
public sealed class SliderFileTests
{
    private string _dir = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(P("pieces.json"), """{ "setPieces": ["alpineRange"] }""");
        File.WriteAllText(P("pinned.json"), """{ "setPieces": ["alpineRange"], "time": "goldenHour", "location": "banff" }""");
        File.WriteAllText(P("s.json"), """{ "location": "hokkaido", "season": "autumn", "time": "dusk", "temperature": 3, "visibility": 60 }""");
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    private string P(string name) => Path.Combine(_dir, name);

    private PreviewRequest Request(string recipe, string? sliders = null) => new()
    {
        RecipePath = P(recipe),
        SlidersPath = sliders is null ? null : P(sliders),
        Seed = 7,
        OutPath = P("p.png"),
        Width = 32,
        Height = 18,
        SamplesPerPixel = 4,
        DryRun = true,
        Grid = TestWorlds.Grid,
        Erosion = false,
    };

    private static PreviewRequest Parse(params string[] args)
    {
        Assert.IsTrue(PreviewCommandLine.TryParse(["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. args],
            out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
        return request!;
    }

    private static string Errors(params string[] args)
    {
        Assert.IsFalse(PreviewCommandLine.TryParse(["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. args],
            out _, out IReadOnlyList<string> errors));
        return string.Join(" | ", errors);
    }

    [TestMethod]
    public void TheWeatherOptions_PinTheirSliders()
    {
        PreviewRequest r = Parse("--sliders", "s.json", "--season", "Winter", "--temperature", "-4.5", "--humidity", "85", "--snowline", "1800",
            "--visibility", "30");
        Assert.AreEqual("s.json", r.SlidersPath);
        Assert.AreEqual("\"winter\"", r.SliderOverrides["season"]);
        Assert.AreEqual("-4.5", r.SliderOverrides["temperature"]);
        Assert.AreEqual("\"celsius\"", r.SliderOverrides["temperatureUnit"]);
        Assert.AreEqual("85", r.SliderOverrides["humidity"]);
        Assert.AreEqual("1800", r.SliderOverrides["snowline"]);
        Assert.AreEqual("30", r.SliderOverrides["visibility"]);

        PreviewRequest f = Parse("--temperature", "41F");
        Assert.AreEqual(5.0, double.Parse(f.SliderOverrides["temperature"], System.Globalization.CultureInfo.InvariantCulture), 1e-12);
        Assert.AreEqual("\"fahrenheit\"", f.SliderOverrides["temperatureUnit"], "°F in, °F shown");
    }

    [TestMethod]
    [DataRow("--season", "monsoon", "--season must be spring, summer, autumn or winter")]
    [DataRow("--temperature", "warm", "--temperature must be degrees Celsius")]
    [DataRow("--temperature", "200F", "--temperature must be degrees Celsius")]
    [DataRow("--humidity", "120", "--humidity must be a number of % relative humidity in [0, 100]")]
    [DataRow("--snowline", "-1", "--snowline must be a number of metres above sea level in [0, 12000]")]
    [DataRow("--visibility", "0.2", "--visibility must be a number of km in [1, 500]")]
    [DataRow("--altitude", "9000", "--altitude must be a number of metres above sea level in [0, 8000]")]
    public void TheWeatherOptions_AreChecked(string option, string value, string message) => StringAssert.Contains(Errors(option, value), message);

    [TestMethod]
    public void APreview_ReadsItsSliderFile_AndTheSidecarRecordsEverySlider()
    {
        Sidecar sidecar = PreviewPipeline.Run(Request("pieces.json", "s.json"), renderer: null);
        SlidersInfo sliders = sidecar.Sliders;
        Assert.AreEqual("slider file", sliders.PinsFrom);
        Assert.AreEqual(P("s.json"), sliders.File);
        Assert.AreEqual(Astronomy.Place.Hokkaido, sidecar.Place.Place);
        Assert.AreEqual(10, sidecar.Sun.Date.Month, "autumn in the north");
        Assert.AreEqual("dusk: sun at −4°, setting", sidecar.Sun.SolarTimeFrom);
        CollectionAssert.AreEquivalent(
            new[] { "location", "altitude", "season", "date", "time", "solarTime", "temperature", "humidity", "snow", "haze" },
            sliders.Values.Keys.ToArray());
        CollectionAssert.AreEquivalent(new[] { "location", "season", "time", "temperature", "haze" },
            sliders.Values.Where(v => v.Value.Pinned).Select(v => v.Key).ToArray());
        Assert.AreEqual(3.0, sliders.Values["temperature"].Number);
        Assert.AreEqual(60.0, sliders.Values["haze"].Number);
        Assert.AreEqual("Furano (JMA AMeDAS 0021)", sliders.Weather.Station);
        Assert.AreEqual(sidecar.Atmosphere.Model.AerosolOpticalDepth550, sliders.Weather.AerosolOpticalDepth550, "the render's air has the sliders' haze");

        // The sidecar reads back in the strict dialect, sliders and all.
        Sidecar back = VistaJson.Deserialize<Sidecar>(File.ReadAllText(P("p.json")));
        Assert.AreEqual(sliders.Pins, back.Sliders.Pins);
        Assert.AreEqual(sliders.Values["snow"], back.Sliders.Values["snow"]);
    }

    [TestMethod]
    public void TheRecipesOwnFields_ArePins_ButNotBesideASliderFile()
    {
        Sidecar own = PreviewPipeline.Run(Request("pinned.json"), renderer: null);
        Assert.AreEqual("recipe", own.Sliders.PinsFrom);
        Assert.AreEqual(Astronomy.Place.Banff, own.Place.Place);
        Assert.IsTrue(own.Sliders.Values["time"].Pinned);

        Sidecar none = PreviewPipeline.Run(Request("pieces.json"), renderer: null);
        Assert.AreEqual("none", none.Sliders.PinsFrom);
        Assert.IsTrue(none.Sliders.Values.Values.All(v => !v.Pinned));

        InvalidDataException e = Assert.ThrowsExactly<InvalidDataException>(() => PreviewPipeline.Run(Request("pinned.json", "s.json"), renderer: null));
        StringAssert.Contains(e.Message, "the recipe pins sliders itself");
    }

    [TestMethod]
    public void Options_PinOnTopOfTheSliderFile()
    {
        PreviewRequest request = Request("pieces.json", "s.json") with
        {
            SliderOverrides = new Dictionary<string, string> { ["temperature"] = "-8", ["location"] = "\"yosemite\"" },
        };
        Sidecar sidecar = PreviewPipeline.Run(request, renderer: null);
        Assert.AreEqual("slider file and options", sidecar.Sliders.PinsFrom);
        Assert.AreEqual(-8.0, sidecar.Sliders.Pins.Temperature);
        Assert.AreEqual(Astronomy.Place.Yosemite, sidecar.Place.Place);
        Assert.AreEqual(TimeOfDay.Dusk, sidecar.Sliders.Pins.Time, "the file's other pins stay");

        Assert.ThrowsExactly<InvalidDataException>(() => PreviewPipeline.Run(
            Request("pieces.json", "s.json") with { SliderOverrides = new Dictionary<string, string> { ["humidity"] = "150" } }, renderer: null));
    }

    [TestMethod]
    public void ASheet_VariesASlider_AndLabelsItsCells()
    {
        Assert.IsTrue(SheetCommandLine.TryParse(["--sheet", "--recipe", P("pieces.json"), "--seed", "7", "--out", P("s.png"), "--dry-run",
            "--grid", "65x512", "--erosion", "off", "--vary", "sunElevation=2,4,6", "--time", "goldenHour"], out SheetRequest? sheet, out IReadOnlyList<string> errors),
            string.Join(" | ", errors));
        SheetSidecar result = SheetPipeline.Render(sheet!, renderer: null);
        double[] heights = [.. result.Cells.Select(c => c.Sidecar!["sun"]!["trueElevationDegrees"]!.GetValue<double>())];
        CollectionAssert.AreEqual(new[] { 2.0, 4.0, 6.0 }, heights.Select(h => Math.Round(h, 6)).ToArray());
        CollectionAssert.AreEqual(new[] { "sunElevation=2", "sunElevation=4", "sunElevation=6" }, result.Cells.Select(c => c.Label).ToArray());
        StringAssert.Contains(result.Title, "summer", "the cells share the season; the sun's height replaced the preset's");

        Assert.IsFalse(SheetCommandLine.TryParse(["--sheet", "--recipe", "r.json", "--seed", "7", "--out", "s.png", "--sliders", "a.json",
            "--vary", "sliders=a.json,b.json"], out _, out IReadOnlyList<string> clash));
        StringAssert.Contains(string.Join(" | ", clash), "--sliders and --vary sliders=");
        Assert.IsFalse(SheetCommandLine.TryParse(["--sheet", "--recipe", "r.json", "--seed", "7", "--out", "s.png", "--temperature", "3",
            "--vary", "temperature=1,2"], out _, out IReadOnlyList<string> twice));
        StringAssert.Contains(string.Join(" | ", twice), "both decide temperature");
    }

    [TestMethod]
    public void Labels_ReadPinsFromTheSlidersOrFromAnOlderRecipe_AsOneKey()
    {
        JsonNode older = JsonNode.Parse("""{ "recipe": { "setPieces": ["alpineRange"], "time": "goldenHour", "season": "summer", "location": "zermatt", "date": "2026-07-15" }, "seed": 42 }""")!;
        JsonNode newer = JsonNode.Parse("""
            { "recipe": { "setPieces": ["alpineRange"], "location": "none" }, "seed": 42,
              "sliders": { "pins": { "location": "zermatt", "date": "2026-07-15", "time": "goldenHour", "temperatureUnit": "celsius" },
                           "values": { "time": { "value": "goldenHour" }, "season": { "value": "summer" } } } }
            """)!;
        SortedDictionary<string, string> a = SheetLabels.Identity(older), b = SheetLabels.Identity(newer);
        CollectionAssert.AreEquivalent(a.ToArray(), b.ToArray(), "a before/after of the same view across builds shows no difference");
        Assert.AreEqual("goldenHour", a["sliders.time"]);
        Assert.IsFalse(a.ContainsKey("sliders.season"), "the date decides the season");

        JsonNode auto = JsonNode.Parse("""
            { "recipe": { "setPieces": ["alpineRange"] }, "sliders": { "pins": { "temperature": 4 },
              "values": { "time": { "value": "day" }, "season": { "value": "summer" }, "humidity": { "value": "60%" } } } }
            """)!;
        SortedDictionary<string, string> c = SheetLabels.Identity(auto);
        Assert.AreEqual("4", c["sliders.temperature"]);
        Assert.AreEqual("summer", c["sliders.season"], "the season names the picture, pinned or not");
        Assert.IsFalse(c.ContainsKey("sliders.humidity"), "an auto weather slider follows the rest");
        StringAssert.Contains(SheetLabels.Title([c]), "4 °C");
    }

    [TestMethod]
    public void AWatch_RendersAgain_WhenTheSliderFileChanges()
    {
        var watch = new WatchRequest
        {
            Values = new Dictionary<string, string>
            {
                ["--recipe"] = P("pieces.json"), ["--sliders"] = P("s.json"), ["--seed"] = "7", ["--out"] = P("w.png"),
                ["--width"] = "32", ["--grid"] = "65x512", ["--erosion"] = "off",
            },
            Flags = new HashSet<string> { "--dry-run" },
        };
        var time = new ManualTime();
        var preview = new PreviewWatch(watch, new PreviewSession(renderer: null, time: time), time);
        WatchEvent first = preview.Poll();
        Assert.AreEqual(WatchEventKind.Rendered, first.Kind, first.Message);
        CollectionAssert.Contains(preview.Watched.ToArray(), P("s.json"));

        File.WriteAllText(P("s.json"), """{ "location": "hokkaido", "season": "autumn", "time": "dusk", "temperature": -6, "visibility": 60 }""");
        Assert.AreEqual(WatchEventKind.Idle, preview.Poll().Kind, "the change settles first");
        time.Advance(PreviewWatch.Settle);
        WatchEvent second = preview.Poll();
        Assert.AreEqual(WatchEventKind.Rendered, second.Kind, second.Message);
        CollectionAssert.AreEqual(new[] { "sliders.temperature: 3 → -6" }, second.Sidecar!.Watch!.Changes.ToArray());
        Assert.AreEqual(-6.0, second.Sidecar.Sliders.Pins.Temperature);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
