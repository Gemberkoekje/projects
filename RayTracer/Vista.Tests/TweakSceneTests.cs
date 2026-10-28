using Vista.Astronomy;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Scene;
using Vista.Sliders;
using Vista.Tweak;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The window's scene (vista-plan §10 M1.16): what starts a new scene, the CLI's own files saved and read back, a run's arguments as the
/// CLI parses them, the camera's nudges and turns, and where its collision lets it stand.
/// </summary>
[TestClass]
public sealed class TweakSceneTests
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

    private string P(string name) => Path.Combine(_dir, name);

    private static readonly CameraSpec Camera = new() { Position = new WorldPoint(10, 1200, -300), YawDegrees = 30, PitchDegrees = 2, FocalLengthMm = 50 };

    private static TweakScene Hero => new()
    {
        Recipe = new Recipe { Name = "alpine range", SetPieces = [SetPiece.AlpineRange] },
        Seed = 42,
        Camera = Camera,
        Pins = new SliderPins { Time = TimeOfDay.GoldenHour, Temperature = 4.5 },
    };

    [TestMethod]
    public void SetPiecesAndSeed_StartANewScene_TheRestKeepsTheCamera()
    {
        TweakScene hero = Hero;
        Assert.IsTrue(hero.With(SetPiece.Lake).IsNew);
        Assert.IsTrue(hero.Without(SetPiece.AlpineRange).IsNew);
        Assert.IsTrue(hero.WithSeed(7).IsNew);
        Assert.AreSame(hero, hero.With(SetPiece.AlpineRange), "a piece already there changes nothing");
        Assert.AreSame(hero, hero.Without(SetPiece.Sea));
        Assert.AreSame(hero, hero.WithSeed(42));
        Assert.IsFalse(hero.Reset(Slider.Temperature).IsNew, "a slider never moves the world");
        Assert.IsNull(hero.Reset(Slider.Temperature).Pins.Temperature);
        CollectionAssert.AreEquivalent(new[] { SetPiece.AlpineRange, SetPiece.FrozenLake }, hero.With(SetPiece.Lake).With(SetPiece.FrozenLake).Recipe.SetPieces.ToArray(),
            "a frozen lake swaps the lake out");
        ulong next = hero.NextSeed(new Random(1));
        Assert.AreNotEqual(42UL, next);
        Assert.IsTrue(next < 1_000_000);
    }

    [TestMethod]
    public void From_GivesARecipesOwnPinsToTheSliders()
    {
        Recipe old = VistaJson.ParseRecipe("""{ "schema": 2, "name": "old", "setPieces": ["snowCappedMountains"], "time": "goldenHour", "season": "summer", "location": "zermatt", "date": "2026-07-15" }""");
        TweakScene scene = TweakScene.From(old, 42);
        Assert.IsFalse(scene.Recipe.HasPins);
        Assert.AreEqual(Recipe.CurrentSchema, scene.Recipe.Schema);
        Assert.AreEqual("old", scene.Recipe.Name);
        Assert.AreEqual(SliderPins.FromRecipe(old), scene.Pins, "the same pins the CLI reads from the recipe");
        Assert.ThrowsExactly<InvalidDataException>(() => TweakScene.From(old, 42, pins: SliderPins.None), "pins in two homes");
        Assert.AreEqual(SliderPins.None, TweakScene.From(new Recipe { SetPieces = [SetPiece.AlpineRange] }, 1).Pins);
    }

    [TestMethod]
    public void Files_RoundTrip_AndWriteOnlyWhatChanged()
    {
        TweakScene scene = Hero with { Preview = new TweakPreview { Width = 480, SamplesPerPixel = 256 } };
        TweakPaths paths = TweakPaths.Of(_dir, "hero");
        Assert.IsTrue(TweakFiles.Write(scene, paths));
        Assert.IsFalse(TweakFiles.Write(scene, paths), "nothing changed, nothing written");
        Assert.AreEqual("hero", paths.Name);

        TweakScene read = TweakFiles.Read(paths.Recipe, out IReadOnlyList<string> ignored);
        Assert.AreEqual(0, ignored.Count);
        Assert.IsTrue(read.Recipe.ContentEquals(scene.Recipe));
        Assert.AreEqual(scene.Seed, read.Seed);
        Assert.AreEqual(scene.Camera, read.Camera);
        Assert.AreEqual(scene.Pins, read.Pins);
        Assert.AreEqual((480, 256), (read.Preview.Width, read.Preview.SamplesPerPixel));

        Assert.IsTrue(TweakFiles.Write(scene.WithSeed(7), paths), "a new scene's camera file goes");
        Assert.IsFalse(File.Exists(paths.Camera));
        Assert.IsTrue(TweakFiles.Read(paths.Recipe, out _).IsNew);
    }

    [TestMethod]
    public void Read_AnOptionsFileKeepsTheSeedAndSizes_AndListsTheRest()
    {
        TweakPaths paths = TweakPaths.Of(_dir, "w");
        TweakFiles.Write(Hero, paths);
        File.WriteAllText(paths.Options, "# a watch's options\n--seed 9 --width 640 --debug all --denoise\n");
        TweakScene read = TweakFiles.Read(paths.Recipe, out IReadOnlyList<string> ignored);
        Assert.AreEqual((9UL, 640, 64), (read.Seed, read.Preview.Width, read.Preview.SamplesPerPixel));
        CollectionAssert.AreEqual(new[] { "--debug", "--denoise" }, ignored.ToArray());
        File.WriteAllText(paths.Options, "--seed nine");
        Assert.ThrowsExactly<InvalidDataException>(() => TweakFiles.Read(paths.Recipe, out _));
    }

    [TestMethod]
    public void Arguments_AreTheCliRun_ForEachKind()
    {
        TweakPaths paths = TweakPaths.Of(_dir, "s");
        TweakScene scene = Hero with
        {
            Preview = new TweakPreview { Width = 480, SamplesPerPixel = 16, View = TweakView.Material },
            Final = new TweakFinal { Width = 2560, SamplesPerPixel = 256, Denoise = true, NoiseTarget = 0.5 },
        };
        PreviewRequest preview = TweakFiles.Request(TweakFiles.Arguments(scene, paths, TweakRun.Preview, P("p.png")));
        Assert.AreEqual((480, 270, 16), (preview.Width, preview.Height, preview.SamplesPerPixel));
        Assert.AreEqual(paths.Camera, preview.CameraPath);
        Assert.AreEqual(paths.Sliders, preview.SlidersPath);
        Assert.IsTrue(preview.Debug.SetEquals([DebugOutput.Material]));
        Assert.IsNull(preview.Denoise);
        Assert.IsNull(preview.AutoCamera);

        PreviewRequest auto = TweakFiles.Request(TweakFiles.Arguments(scene, paths, TweakRun.AutoCamera, P("p.png")));
        Assert.IsNotNull(auto.AutoCamera);
        Assert.IsNull(auto.CameraPath, "the auto camera chooses the camera");

        PreviewRequest denoised = TweakFiles.Request(TweakFiles.Arguments(scene with { Preview = scene.Preview with { View = TweakView.Denoised } }, paths, TweakRun.Preview, P("p.png")));
        Assert.AreEqual(DenoiseDevice.Cuda, denoised.Denoise!.Device);
        Assert.AreEqual(0, denoised.Debug.Count);

        PreviewRequest estimate = TweakFiles.Request(TweakFiles.Arguments(scene, paths, TweakRun.Estimate, P("f.png")));
        Assert.AreEqual((2560, 1440, 256), (estimate.Width, estimate.Height, estimate.SamplesPerPixel));
        Assert.IsTrue(estimate.Estimate);
        Assert.AreEqual(0.5, estimate.NoiseTarget);
        Assert.IsNotNull(estimate.Denoise);
        Assert.AreEqual(0, estimate.Debug.Count, "the final takes no debug image");

        PreviewRequest final = TweakFiles.Request(TweakFiles.Arguments(scene, paths, TweakRun.Final, P("f.png")));
        Assert.IsFalse(final.Estimate);
        Assert.AreEqual(2560, final.Width);

        PreviewRequest fresh = TweakFiles.Request(TweakFiles.Arguments(scene.WithSeed(5), paths, TweakRun.Preview, P("p.png")));
        Assert.IsNull(fresh.CameraPath, "a new scene's run chooses its camera");
        Assert.AreEqual(5UL, fresh.Seed);
    }

    [TestMethod]
    public void CommandLine_QuotesWhatHoldsASpace()
    {
        Assert.AreEqual("Vista.App --preview --recipe \"my scenes/a.recipe.json\" --seed 1",
            TweakFiles.CommandLine(["--preview", "--recipe", "my scenes/a.recipe.json", "--seed", "1"]));
    }

    [TestMethod]
    public void Paths_BesideARecipe()
    {
        TweakPaths paths = TweakPaths.Beside(P("alps.recipe.json"));
        Assert.AreEqual(P("alps.camera.json"), paths.Camera);
        Assert.AreEqual(P("alps.sliders.json"), paths.Sliders);
        Assert.AreEqual(P("alps.options.txt"), paths.Options);
        Assert.AreEqual(P("old.camera.json"), TweakPaths.Beside(P("old.json")).Camera, "a plain .json recipe names its siblings too");
    }

    [TestMethod]
    public void Nudge_MovesAlongTheHeading_Level()
    {
        CameraSpec east = Camera with { YawDegrees = 90, PitchDegrees = 30, RollDegrees = 10 };
        WorldPoint p = CameraMoves.Nudge(east, 10, 0, 0).Position;
        Assert.AreEqual(Camera.Position.X + 10, p.X, 1e-9, "forward is east, level whatever the pitch");
        Assert.AreEqual(Camera.Position.Y, p.Y, 1e-9);
        Assert.AreEqual(Camera.Position.Z, p.Z, 1e-9);
        WorldPoint r = CameraMoves.Nudge(east, 0, 5, 2).Position;
        Assert.AreEqual(Camera.Position.Z - 5, r.Z, 1e-9, "right of east is south");
        Assert.AreEqual(Camera.Position.Y + 2, r.Y, 1e-9);
    }

    [TestMethod]
    public void Turn_WrapsTheHeadingAndRoll_AndStopsShortOfStraightUp()
    {
        CameraSpec turned = CameraMoves.Turn(Camera with { YawDegrees = 350, RollDegrees = 175 }, 15, 200, 10);
        Assert.AreEqual(5.0, turned.YawDegrees, 1e-9);
        Assert.AreEqual(CameraMoves.MaxPitchDegrees, turned.PitchDegrees);
        Assert.AreEqual(-175.0, turned.RollDegrees, 1e-9);
        Assert.AreEqual(0, CameraMoves.Turn(turned, 0, -400, 0).Validate().Count, "a turned camera is always a valid camera file");
    }

    [TestMethod]
    public void Ground_LiftsTheCamera_ClearOfTheGroundAndTheForest()
    {
        var heights = new float[65 * 65];
        for (int k = 0; k < heights.Length; k++)
            heights[k] = (float)(k % 65 * 2.0);
        var surface = new HeightField(65, 10.0, heights);
        var ground = new CameraGround(surface, DetailMetres: 3.0, CanopyMetres: 25.0, TreeLineHeight: 60.0);
        // At x = 0 (i = 32) the grid stands 64 m: above the tree line, so the rock's detail only.
        Assert.AreEqual(64 + 3 + CameraGround.EyeMetres, ground.FloorAt(0, 0), 1e-4);
        // At x = −200 (i = 12) it stands 24 m: forest may grow, so its crowns.
        Assert.AreEqual(24 + 25 + CameraGround.EyeMetres, ground.FloorAt(-200, 0), 1e-4);

        CameraSpec low = Camera with { Position = new WorldPoint(-200, 10, 0) };
        CameraSpec lifted = ground.Clamp(low, out string? why);
        Assert.AreEqual(ground.FloorAt(-200, 0), lifted.Position.Y, 1e-9);
        StringAssert.Contains(why, "forest");
        CameraSpec high = Camera with { Position = new WorldPoint(0, 500, 0) };
        Assert.AreSame(high, ground.Clamp(high, out string? none));
        Assert.IsNull(none);
        CameraSpec far = ground.Clamp(Camera with { Position = new WorldPoint(10_000, 500, 0) }, out string? edge);
        Assert.AreEqual(320.0, far.Position.X, "kept over the world's grid");
        StringAssert.Contains(edge, "edge");
    }

    [TestMethod]
    public void Ground_ReadsTheRunsDetailForestAndAltitude()
    {
        var surface = new HeightField(9, 10.0, new float[81]);
        var materials = new MaterialsInfo
        {
            ByRule = true,
            Table = [],
            Forest = true,
            TreeLineMetres = 2100,
            Detail = new MaterialDetailInfo(4.0, 2.5, 1.0, 0, 0, 0) { CanopyAmplitudeMetres = 22 },
        };
        CameraGround ground = CameraGround.Of(surface, materials, groundAltitudeMetres: 1500);
        Assert.AreEqual((4.0, 22.0, 600.0), (ground.DetailMetres, ground.CanopyMetres, ground.TreeLineHeight));
        CameraGround bare = CameraGround.Of(surface, materials with { Forest = false, Detail = null }, 1500);
        Assert.AreEqual((0.0, 0.0), (bare.DetailMetres, bare.CanopyMetres), "no forest, no detail: eye height on the grid");
    }
}
