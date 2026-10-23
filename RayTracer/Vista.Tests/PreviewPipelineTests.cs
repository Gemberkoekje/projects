using System.Text.Json;
using Vista.Astronomy;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>
/// The headless preview end to end, minus the GPU (vista-plan §7.4): a dry run, and a full run against a
/// fake renderer that honours the <see cref="IVistaRenderer"/> contract, so every file the real renderer
/// will produce is written and checked here.
/// </summary>
[TestClass]
public sealed class PreviewPipelineTests
{
    private string _dir = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "r.json"), """{ "setPieces": ["alpineRange"], "time": "goldenHour" }""");
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    private PreviewRequest Request(bool dryRun, params DebugOutput[] debug) => new()
    {
        RecipePath = Path.Combine(_dir, "r.json"),
        Seed = 7,
        OutPath = Path.Combine(_dir, "p.png"),
        Width = 32,
        Height = 18,
        SamplesPerPixel = 4,
        Debug = debug.ToHashSet(),
        DryRun = dryRun,
        Grid = TestWorlds.Grid,
        Erosion = false,
    };

    [TestMethod]
    public void DryRun_WritesSidecarAndHeightMapOnly()
    {
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: true), renderer: null);

        Assert.AreEqual("dryRun", sidecar.Kind);
        Assert.IsNull(sidecar.Render);
        Assert.IsNull(sidecar.Engine.Device);
        Assert.IsTrue(sidecar.DefaultCamera);
        Assert.AreEqual("mountains", sidecar.World.TerrainProfile);
        Assert.AreEqual(TestWorlds.Grid.Size, sidecar.World.GridSize);
        // The ground's six materials by rule (M1.12, M1.12b's forest), all built in; a dry run shows them from above.
        CollectionAssert.AreEquivalent(new[] { "forest", "grass", "rock.hard", "rock.soft", "scree", "soil" }, sidecar.DataTiers.Keys.ToArray());
        Assert.IsTrue(sidecar.DataTiers.Values.All(t => t == DataTier.BuiltIn));
        CollectionAssert.AreEquivalent(new[] { "heightMap", "materialMap", "sidecar" }, sidecar.Outputs.Keys.ToArray());
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "p.materials.png")));
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "p.heightmap.png")));
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "p.png")));
        Assert.IsTrue(sidecar.TimingsSeconds.ContainsKey("worldGen"));
    }

    [TestMethod]
    public void DryRun_IsReproducible()
    {
        Sidecar a = PreviewPipeline.Run(Request(dryRun: true), renderer: null);
        Sidecar b = PreviewPipeline.Run(Request(dryRun: true), renderer: null);
        // The same world and mesh; only what building them took may differ.
        static WorldInfo Untimed(WorldInfo world) => world with { Mesh = world.Mesh! with { RefineSeconds = 0, VertexSeconds = 0, HierarchySeconds = 0 } };
        Assert.AreEqual(Untimed(a.World), Untimed(b.World));
        Assert.AreEqual(a.Camera, b.Camera);
        CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(_dir, "p.heightmap.png")), PngEncoder.Encode(
            DebugImages.HeightMap(World.TerrainGenerator.Generate(a.Recipe, 7, TestWorlds.Grid), a.Camera).Rgb, TestWorlds.Grid.Size, TestWorlds.Grid.Size, PngColor.Rgb));
    }

    [TestMethod]
    public void Sidecar_RoundTripsThroughTheStrictDialect()
    {
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: false, DebugOutput.Depth), new FakeRenderer());
        string json = File.ReadAllText(Path.Combine(_dir, "p.json"));
        Sidecar back = VistaJson.Deserialize<Sidecar>(json);
        Assert.AreEqual(sidecar.World, back.World);
        Assert.AreEqual(sidecar.Camera, back.Camera);
        Assert.IsTrue(sidecar.Recipe.ContentEquals(back.Recipe));
        Assert.AreEqual(sidecar.Render!.Stats.RaysTraced, back.Render!.Stats.RaysTraced);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.AreEqual("builtIn", doc.RootElement.GetProperty("dataTiers").GetProperty("rock.hard").GetString());
        Assert.AreEqual(7ul, doc.RootElement.GetProperty("seed").GetUInt64());
    }

    [TestMethod]
    public void Render_WritesBeautyExrDebugAndSidecar()
    {
        var renderer = new FakeRenderer();
        PreviewRequest request = Request(dryRun: false, Enum.GetValues<DebugOutput>());
        var time = new FixedTime(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
        Sidecar sidecar = PreviewPipeline.Run(request, renderer, time);

        Assert.AreEqual("preview", sidecar.Kind);
        Assert.AreEqual("2026-10-18T12:00:00.0000000Z", sidecar.CreatedUtc);
        Assert.AreEqual(FakeRenderer.Name, sidecar.Engine.Device);
        Assert.AreEqual(32 * 18 * 4L, sidecar.Render!.Stats.PixelSamples);
        Assert.AreEqual(3.0, sidecar.Render.RaysPerSample, 1e-12);
        Assert.IsTrue(renderer.Planner!.IsComplete, "the renderer drove the planner to completion");
        CollectionAssert.AreEquivalent(new[] { Aov.Albedo, Aov.Normal, Aov.Depth, Aov.MaterialId, Aov.Variance }, renderer.Requested!.ToArray());

        foreach (string file in sidecar.Outputs.Values)
            Assert.IsTrue(File.Exists(Path.Combine(_dir, file)), file);
        CollectionAssert.AreEquivalent(
            new[] { "albedo", "beauty", "beautyExr", "depth", "material", "normal", "sidecar", "tier", "variance" },
            sidecar.Outputs.Keys.ToArray());
        byte[] png = File.ReadAllBytes(Path.Combine(_dir, "p.png"));
        Assert.AreEqual(0x89, png[0]);
        Assert.AreEqual(20000630, BitConverter.ToInt32(File.ReadAllBytes(Path.Combine(_dir, "p.exr")), 0));
    }

    [TestMethod]
    public void DispatchBudget_ReachesThePlannerAndTheSidecar()
    {
        var renderer = new FakeRenderer();
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: false) with { DispatchBudgetMs = 40 }, renderer);
        Assert.AreEqual(40.0, renderer.Settings!.Batching.DispatchBudgetMs);
        Assert.AreEqual(40.0, sidecar.Render!.DispatchBudgetMs);
    }

    [TestMethod]
    public void Render_PassesTheSuppliedCamera()
    {
        string cameraPath = Path.Combine(_dir, "c.json");
        CameraSpec camera = new() { Position = new WorldPoint(100, 5000, -2000), YawDegrees = 45, PitchDegrees = -10 };
        File.WriteAllText(cameraPath, VistaJson.Serialize(camera));
        var renderer = new FakeRenderer();
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: false) with { CameraPath = cameraPath }, renderer);
        Assert.AreEqual(camera, renderer.Scene!.Camera);
        Assert.IsFalse(sidecar.DefaultCamera);
        Assert.AreEqual(0, sidecar.Warnings.Count);
    }

    [TestMethod]
    public void UndergroundCamera_IsAWarningNotAnError()
    {
        string cameraPath = Path.Combine(_dir, "c.json");
        File.WriteAllText(cameraPath, VistaJson.Serialize(new CameraSpec { Position = new WorldPoint(0, -500, 0) }));
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: true) with { CameraPath = cameraPath }, renderer: null);
        StringAssert.Contains(sidecar.Warnings.Single(), "below the terrain");
    }

    [TestMethod]
    public void MissingAov_FailsLoudly()
    {
        var e = Assert.ThrowsExactly<InvalidOperationException>(() =>
            PreviewPipeline.Run(Request(dryRun: false, DebugOutput.Normal), new FakeRenderer { Skip = Aov.Normal }));
        StringAssert.Contains(e.Message, "Normal");
    }

    [TestMethod]
    public void FullFrame_RecordsTheWholeFrameAsItsWindow()
    {
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: false), new FakeRenderer());
        Assert.AreEqual(PixelWindow.Full(32, 18), sidecar.Image.Window);
        Assert.AreEqual(Sidecar.CurrentSchema, sidecar.Schema);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "p.json")));
        JsonElement window = doc.RootElement.GetProperty("image").GetProperty("window");
        CollectionAssert.AreEqual(new[] { "x", "y", "width", "height" }, window.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [TestMethod]
    public void Loupe_RendersOnlyItsWindowOfTheFrame()
    {
        var renderer = new FakeRenderer();
        var loupe = new PixelWindow(5, 3, 8, 6);
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: false, DebugOutput.Depth) with { Loupe = loupe }, renderer);

        Assert.AreEqual((32, 18), (renderer.Settings!.Width, renderer.Settings.Height), "the camera keeps the full frame");
        Assert.AreEqual(loupe, renderer.Settings.Rendered);
        Assert.AreEqual(8 * 6 * 4L, sidecar.Render!.Stats.PixelSamples);
        Assert.AreEqual(loupe, sidecar.Image.Window);
        Assert.AreEqual((32, 18), (sidecar.Image.Width, sidecar.Image.Height));
        foreach (string file in new[] { "p.png", "p.depth.png" })
        {
            PngImage image = PngDecoder.Read(Path.Combine(_dir, file));
            Assert.AreEqual((8, 6), (image.Width, image.Height), file);
        }
    }

    [TestMethod]
    public void Loupe_IsTheSamePixelsAsTheFullFrame()
    {
        PreviewPipeline.Run(Request(dryRun: false) with { OutPath = Path.Combine(_dir, "full.png") }, new FakeRenderer());
        var loupe = new PixelWindow(5, 3, 8, 6);
        PreviewPipeline.Run(Request(dryRun: false) with { OutPath = Path.Combine(_dir, "loupe.png"), Loupe = loupe }, new FakeRenderer());

        PngImage full = PngDecoder.Read(Path.Combine(_dir, "full.png"));
        PngImage window = PngDecoder.Read(Path.Combine(_dir, "loupe.png"));
        for (int y = 0; y < loupe.Height; y++)
        {
            for (int x = 0; x < loupe.Width * 3; x++)
                Assert.AreEqual(full.Pixels[((loupe.Y + y) * 32 + loupe.X) * 3 + x], window.Pixels[y * loupe.Width * 3 + x]);
        }
    }

    [TestMethod]
    public void Loupe_OutsideTheFrame_IsRejected() =>
        Assert.ThrowsExactly<ArgumentException>(() =>
            PreviewPipeline.Run(Request(dryRun: false) with { Loupe = new PixelWindow(30, 0, 8, 6) }, new FakeRenderer()));

    [TestMethod]
    public void RecipeOverrides_ChangeTheRecipeBeforeTheStrictParse()
    {
        var renderer = new FakeRenderer();
        Sidecar sidecar = PreviewPipeline.Run(
            Request(dryRun: false) with { RecipeOverrides = new Dictionary<string, string> { ["time"] = "\"dawn\"" } }, renderer);
        Assert.AreEqual(TimeOfDay.Dawn, sidecar.Recipe.Time);
        Assert.AreEqual(TimeOfDay.Dawn, renderer.Scene!.Recipe.Time);
        CollectionAssert.AreEqual(new[] { SetPiece.AlpineRange }, sidecar.Recipe.SetPieces.ToArray(), "the rest of the file is kept");

        Assert.ThrowsExactly<JsonException>(() => PreviewPipeline.Run(
            Request(dryRun: true) with { RecipeOverrides = new Dictionary<string, string> { ["tme"] = "\"dawn\"" } }, renderer: null));
    }

    [TestMethod]
    public void CameraOverrides_ApplyToTheDefaultCamera()
    {
        Sidecar plain = PreviewPipeline.Run(Request(dryRun: true), renderer: null);
        Sidecar turned = PreviewPipeline.Run(
            Request(dryRun: true) with { CameraOverrides = new Dictionary<string, string> { ["yawDegrees"] = "90" } }, renderer: null);
        Assert.AreEqual(90.0, turned.Camera.YawDegrees);
        Assert.AreEqual(plain.Camera.Position, turned.Camera.Position, "the default pose is kept");
        Assert.IsFalse(turned.DefaultCamera, "an overridden camera is no longer the default one");
    }

    [TestMethod]
    public void Session_ReusesTheWorldAcrossTimeOfDay()
    {
        var renderer = new FakeRenderer();
        var session = new PreviewSession(renderer);
        session.Run(Request(dryRun: false));
        session.Run(Request(dryRun: false) with { RecipeOverrides = new Dictionary<string, string> { ["time"] = "\"dusk\"" } });
        Assert.AreSame(renderer.Scenes[0].Mesh, renderer.Scenes[1].Mesh);
        Assert.AreSame(renderer.Scenes[0].Terrain, renderer.Scenes[1].Terrain);
    }

    [TestMethod]
    public void Sidecar_RecordsThePlaceAndTheSun()
    {
        var renderer = new FakeRenderer();
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: false), renderer);

        Assert.AreEqual(Place.Zermatt, sidecar.Place.Place, "the default place");
        Assert.AreEqual(new DateOnly(SeasonCalendar.ReferenceYear, 7, 15), sidecar.Sun.Date);
        Assert.AreEqual("season summer", sidecar.Sun.DateFrom);
        Assert.AreEqual("goldenHour: sun at +2°, setting", sidecar.Sun.SolarTimeFrom);
        Assert.AreEqual(DaylightKind.RisesAndSets, sidecar.Sun.Daylight);
        Almanac almanac = Almanac.For(VistaJson.ParseRecipe(File.ReadAllText(Path.Combine(_dir, "r.json"))));
        Assert.AreEqual(almanac.Sun, renderer.Scene!.Sun, "the renderer gets the almanac's sun");
        Assert.AreEqual(almanac.Sun.AzimuthDegrees, sidecar.Sun.AzimuthDegrees);
        Assert.AreEqual(almanac.Sun.ElevationDegrees, sidecar.Sun.ElevationDegrees);
        Assert.AreEqual(Almanac.FormatHours(almanac.SolarHours), sidecar.Sun.SolarTime);
        Assert.AreEqual(Almanac.GoldenHourElevationDegrees, sidecar.Sun.TrueElevationDegrees, 1e-6, "golden hour is a sun height (M1.11)");
        Assert.AreEqual(Almanac.FormatHours(almanac.Daylight.SunsetHours), sidecar.Sun.Sunset);
        Assert.AreEqual(-almanac.Sun.ToSun.Y, sidecar.Sun.Direction.Y, 1e-6, "the direction is the sunlight's travel");

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "p.json")));
        JsonElement place = doc.RootElement.GetProperty("place");
        Assert.AreEqual("zermatt", place.GetProperty("place").GetString());
        Assert.IsFalse(place.TryGetProperty("isCustom", out _), "derived flags stay out of the file");
        Assert.AreEqual("2026-07-15", doc.RootElement.GetProperty("sun").GetProperty("date").GetString());
        Sidecar back = VistaJson.Deserialize<Sidecar>(File.ReadAllText(Path.Combine(_dir, "p.json")));
        Assert.AreEqual(sidecar.Place, back.Place, "the place reads back");
        Assert.AreEqual(sidecar.Sun, back.Sun, "the sun reads back");
    }

    [TestMethod]
    public void PlaceOverrides_ReachTheSun_AndPolarDayHints()
    {
        var overrides = new Dictionary<string, string>
        {
            ["location"] = "\"lofoten\"", ["date"] = "\"2026-06-21\"", ["time"] = "\"dusk\"",
        };
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: true) with { RecipeOverrides = overrides }, renderer: null);
        Assert.AreEqual(Place.Lofoten, sidecar.Place.Place);
        Assert.AreEqual("pinned date", sidecar.Sun.DateFrom);
        Assert.AreEqual(DaylightKind.PolarDay, sidecar.Sun.Daylight);
        Assert.IsNull(sidecar.Sun.Sunrise);
        Assert.AreEqual("24:00:00", sidecar.Sun.SolarTime, "dusk's lowest sun is the midnight after the evening");
        StringAssert.Contains(sidecar.Warnings.Single(), "never sinks to −4° at Lofoten on 2026-06-21 (polar day)");
    }

    [TestMethod]
    public void NoRenderer_RequiresADryRun() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => PreviewPipeline.Run(Request(dryRun: false), renderer: null));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
