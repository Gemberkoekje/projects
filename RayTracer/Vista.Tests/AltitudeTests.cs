using System.Numerics;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// M1.4's plumbing: the altitude control (recipe schema 4, <c>--altitude</c>), the base-altitude rule, the frame's terrain
/// reach for the bent world, and the schema-5 sidecar.
/// </summary>
[TestClass]
public sealed class AltitudeTests
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

    [TestMethod]
    public void Recipe_ReadsAnAltitude_AndOlderSchemasStillRead()
    {
        Recipe recipe = VistaJson.ParseRecipe("""{ "schema": 4, "setPieces": ["alpineRange"], "altitude": 3000 }""");
        Assert.AreEqual(3000.0, recipe.Altitude);
        Assert.IsNull(VistaJson.ParseRecipe("""{ "schema": 3, "setPieces": ["alpineRange"] }""").Altitude);
        Assert.AreEqual(4, Recipe.CurrentSchema);
        Assert.IsTrue(recipe.ContentEquals(VistaJson.ParseRecipe(VistaJson.Serialize(recipe))), "round trip");
        Assert.IsFalse(recipe.ContentEquals(recipe with { Altitude = 0.0 }));
    }

    [TestMethod]
    public void BaseAltitude_IsTheRecipes_ElseThePlacesValleyFloor()
    {
        PlaceInfo zermatt = Places.Get(Place.Zermatt);
        Assert.AreEqual((1608.0, "valley floor of Zermatt (Zermatt village)"), Places.BaseAltitude(new Recipe(), zermatt));
        Assert.AreEqual((0.0, "recipe altitude"), Places.BaseAltitude(new Recipe { Altitude = 0.0 }, zermatt));
        Assert.AreEqual((0.0, "sea level (custom latitude)"), Places.BaseAltitude(new Recipe(), Places.Custom(10.0)));
        Assert.AreEqual(2500.0, Places.BaseAltitude(new Recipe { Altitude = 2500.0 }, Places.Custom(10.0)).Metres);
    }

    private static PreviewRequest Parse(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" ", errors));
        return request!;
    }

    [TestMethod]
    public void AltitudeOption_OverridesTheRecipe()
    {
        Assert.AreEqual("3000", Parse("--altitude", "3000").SliderOverrides["altitude"]);
        Assert.AreEqual("0", Parse("--altitude", "0").SliderOverrides["altitude"]);
        Assert.IsFalse(Parse().RecipeOverrides.ContainsKey("altitude"));
        foreach (string bad in new[] { "-1", "8001", "high" })
        {
            Assert.IsFalse(PreviewCommandLine.TryParse(["--preview", "--recipe", "r", "--seed", "1", "--out", "p.png", "--altitude", bad], out _, out IReadOnlyList<string> errors));
            StringAssert.Contains(errors[0], "--altitude");
        }
    }

    [TestMethod]
    public void Sheet_RefusesToVaryTheAltitudeTheOptionDecides()
    {
        Assert.IsFalse(SheetCommandLine.TryParse(
            ["--sheet", "--recipe", "r.json", "--seed", "1", "--out", "s.png", "--altitude", "100", "--vary", "altitude=0,3000"], out _, out IReadOnlyList<string> errors));
        Assert.IsTrue(errors.Any(e => e.Contains("altitude", StringComparison.Ordinal)), string.Join(" ", errors));
        Assert.IsTrue(SheetCommandLine.TryParse(["--sheet", "--recipe", "r.json", "--seed", "1", "--out", "s.png", "--vary", "altitude=0,3000"], out _, out _));
    }

    private PreviewRequest Request(string recipeJson, bool dryRun = false)
    {
        File.WriteAllText(Path.Combine(_dir, "r.json"), recipeJson);
        return new PreviewRequest { RecipePath = Path.Combine(_dir, "r.json"), Seed = 7, OutPath = Path.Combine(_dir, "p.png"), Width = 32, Height = 18, SamplesPerPixel = 4, DryRun = dryRun, Grid = TestWorlds.Grid, Erosion = false };
    }

    [TestMethod]
    public void Pipeline_PutsTheSceneAtTheRecipesAltitude_AndSaysWhere()
    {
        var renderer = new FakeRenderer();
        Sidecar sidecar = PreviewPipeline.Run(Request("""{ "setPieces": ["alpineRange"], "location": "zermatt", "altitude": 3000 }"""), renderer);
        Assert.AreEqual(3000.0, renderer.Scene!.GroundAltitudeMetres);
        Assert.AreEqual(3000.0, sidecar.Atmosphere.GroundAltitudeMetres);
        Assert.AreEqual("pinned", sidecar.Atmosphere.GroundAltitudeFrom);
        Assert.AreEqual(AtmosphereInfo.LutAerialPerspective, sidecar.Atmosphere.AerialPerspective);
        Assert.IsTrue(sidecar.Atmosphere.ZenithFromGround.Rayleigh550 < 0.07, "thinner air above 3 km");

        Sidecar valley = PreviewPipeline.Run(Request("""{ "setPieces": ["alpineRange"] }""", dryRun: true), renderer: null);
        // The world's lowest valleys, not its height 0, at the valley floor (M1.12c).
        StringAssert.StartsWith(valley.Atmosphere.GroundAltitudeFrom, "auto: the valley floor of Zermatt (Zermatt village), where the world's lowest valleys stand (its 5th percentile");
        Assert.AreEqual(1608.0, valley.Atmosphere.GroundAltitudeMetres + valley.World.Footing!.HeightMetres, 1e-9);
        Assert.AreEqual(22, valley.Schema);
    }

    [TestMethod]
    public void Sidecar_SaysHowTheReferenceDidTheAir()
    {
        AtmosphereFrame frame = AtmosphereLutsTests.Frame;
        StringAssert.StartsWith(AtmosphereInfo.From(frame, AtmosphereMode.Reference, "x").AerialPerspective, "delta tracking");
        StringAssert.Contains(AtmosphereInfo.From(frame, AtmosphereMode.ReferenceShadowed, "x").AerialPerspective, "terrain's shadow");
        StringAssert.StartsWith(AtmosphereInfo.From(frame, AtmosphereMode.Luts, "x").AerialPerspective, "march");
    }

    [TestMethod]
    public void Frame_ReachesAsFarAsTheMesh()
    {
        // Without the far field the mesh reaches the world grid's farthest corner (M1.4's reach); with it, past the horizon (M1.9).
        var flat = new HeightField(9, 250.0, new float[81]); // a 2 km tile centred on the origin
        var camera = new CameraSpec { Position = new WorldPoint(300.0, 50.0, -400.0) };
        var sun = new SunPosition(0.0, 10.0, 10.0, 0.0, 0.0);
        double radius = AtmosphereModel.Default.PlanetRadiusMetres + 1000.0;
        TerrainHierarchy hierarchy = TerrainHierarchy.Build(flat, TerrainProfile.Hills, 1, TerrainDetail.None);
        TerrainMesh tile = ViewMesher.Build(hierarchy, MeshView.For(camera, 320, 180, radius), new MeshSettings { FarField = false });
        AtmosphereFrame frame = VistaScene.FrameFor(AtmosphereModel.Default, 1000.0, sun, camera, tile);
        Assert.AreEqual(Math.Sqrt(1300.0 * 1300.0 + 1400.0 * 1400.0), frame.TerrainReachMetres, 1e-9);
        Assert.AreEqual(1000.0, frame.TerrainLowAltitudeMetres, 1e-9);

        TerrainMesh far = ViewMesher.Build(hierarchy, MeshView.For(camera, 320, 180, radius), new MeshSettings());
        AtmosphereFrame beyond = VistaScene.FrameFor(AtmosphereModel.Default, 1000.0, sun, camera, far);
        Assert.AreEqual(far.ReachMetres, beyond.TerrainReachMetres);
        Assert.IsTrue(beyond.TerrainReachMetres > 50_000.0, "the hills' horizon, past the tile");
        Assert.AreEqual(1000.0 + far.MaxHeight, beyond.TerrainHighAltitudeMetres, 1e-9, "the far field's hills, higher than the flat tile");
    }

    [TestMethod]
    public void SunReachesScene_CountsTheBentWorldsTilt()
    {
        // From 1 m up the horizon dips 0.03°; with the sun's upper limb 0.1° below that, nothing sees it — unless the terrain
        // reaches far enough toward the sun for its vertical to tilt by more than 0.1°.
        double radius = AtmosphereModel.Default.PlanetRadiusMetres + 1000.0;
        double dip = Math.Atan2(Math.Sqrt(1.0 * (2.0 * radius + 1.0)), radius);
        double elevation = -(dip + SolarSpectrum.AngularRadiusRadians + 0.1 * Math.PI / 180.0);
        var toSun = new Vector3(0.0f, (float)Math.Sin(elevation), (float)Math.Cos(elevation));
        AtmosphereFrame near = AtmosphereFrame.For(AtmosphereModel.Default, 1000.0, toSun, 1.0, 0.0, 0.0);
        Assert.IsFalse(near.SunReachesScene);
        Assert.IsTrue((near with { TerrainReachMetres = 20_000.0 }).SunReachesScene, "20 km tilts the vertical 0.18°");
    }
}
