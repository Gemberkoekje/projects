using System.Reflection;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// The preview session's stages (vista-plan §10 M1.8): each change rebuilds exactly the stages that depend on it, every render of a
/// session writes the same files a cold render of the same request does, and the render's key covers everything the renderer reads.
/// The fake renderer keeps and reports its stages as the D3D12 host does, and its image depends on the scene, so a stale reuse shows.
/// </summary>
[TestClass]
public sealed class PreviewSessionTests
{
    private static readonly PreviewStage[] Sky = [PreviewStage.SkyView, PreviewStage.SkyDistribution];
    private static readonly PreviewStage[] Image = [PreviewStage.Render, PreviewStage.ExposureMeter];
    private static readonly PreviewStage[] Everything =
    [
        PreviewStage.Device, PreviewStage.World, PreviewStage.Mesh, PreviewStage.AccelerationStructure, PreviewStage.AtmosphereTables,
        PreviewStage.ScatteringOrders, .. Sky, .. Image,
    ];

    private string _dir = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(P("r.json"), """{ "setPieces": ["alpineRange"], "time": "day", "location": "zermatt", "date": "2026-07-15" }""");
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    private string P(string name) => Path.Combine(_dir, name);

    private PreviewRequest Base => new()
    {
        RecipePath = P("r.json"),
        Seed = 7,
        OutPath = P("p.png"),
        Width = 32,
        Height = 18,
        SamplesPerPixel = 4,
        Grid = TestWorlds.Grid,
        Erosion = false,
    };

    private static PreviewRequest Recipe(PreviewRequest request, string field, string json) =>
        request with { RecipeOverrides = new Dictionary<string, string>(request.RecipeOverrides) { [field] = json } };

    private static PreviewRequest Camera(PreviewRequest request, string field, string json) =>
        request with { CameraOverrides = new Dictionary<string, string>(request.CameraOverrides) { [field] = json } };

    [TestMethod]
    public void OneShot_BuildsEveryStage()
    {
        Sidecar sidecar = PreviewPipeline.Run(Base, new FakeRenderer());
        CollectionAssert.AreEqual(Everything, sidecar.Stages.Built.ToArray());
        Assert.AreEqual(0, sidecar.Stages.Reused.Count);
        Assert.IsNull(sidecar.Watch);
    }

    [TestMethod]
    public void DryRun_HasOnlyTheWorldStages()
    {
        Sidecar sidecar = PreviewPipeline.Run(Base with { DryRun = true }, renderer: null);
        CollectionAssert.AreEqual(new[] { PreviewStage.World, PreviewStage.Mesh }, sidecar.Stages.Built.ToArray());
        Assert.AreEqual(0, sidecar.Stages.Reused.Count);
    }

    [TestMethod]
    public void EachChange_RebuildsExactlyItsStages_AndWritesWhatAColdRenderWrites()
    {
        var renderer = new FakeRenderer { SceneDependent = true };
        var session = new PreviewSession(renderer);
        PreviewRequest request = Base;
        var steps = new (string Name, Func<PreviewRequest, PreviewRequest> Change, PreviewStage[] Rebuilds)[]
        {
            ("first render", r => r, Everything),
            ("the same again", r => r, []),
            ("exposure +1 EV", r => Camera(r, "exposureEv", "1"), []),
            ("time of day", r => Recipe(r, "time", "\"goldenHour\""), [.. Sky, .. Image]),
            ("camera turn", r => Camera(r, "yawDegrees", "20"), Image),
            ("camera move", r => Camera(r, "position.y", "3000"), [.. Sky, .. Image]),
            ("base altitude", r => Recipe(r, "altitude", "3000"), [PreviewStage.ScatteringOrders, .. Sky, .. Image]),
            ("seed", r => r with { Seed = 8 }, [PreviewStage.World, PreviewStage.Mesh, PreviewStage.AccelerationStructure, .. Sky, .. Image]),
            ("width", r => r with { Width = 48, Height = 27 }, [PreviewStage.Render]),
            ("width back", r => r with { Width = 32, Height = 18 }, [PreviewStage.Render]),
            ("exposure back", r => Camera(r, "exposureEv", "0"), []),
        };

        foreach ((string name, Func<PreviewRequest, PreviewRequest> change, PreviewStage[] rebuilds) in steps)
        {
            request = change(request);
            Sidecar sidecar = session.Run(request);
            CollectionAssert.AreEqual(rebuilds, sidecar.Stages.Built.ToArray(), $"{name}: built {string.Join(", ", sidecar.Stages.Built)}");
            CollectionAssert.AreEquivalent(Everything.Except(rebuilds).ToArray(), sidecar.Stages.Reused.ToArray(), $"{name}: reused");

            PreviewPipeline.Run(request with { OutPath = P("cold.png") }, new FakeRenderer { SceneDependent = true });
            foreach (string suffix in new[] { ".png", ".exr" })
                CollectionAssert.AreEqual(File.ReadAllBytes(P("cold" + suffix)), File.ReadAllBytes(P("p" + suffix)), $"{name}: {suffix} as a cold render");
        }
        Assert.AreEqual(8, renderer.Scenes.Count, "the first, time, turn, move, altitude, seed and both widths; never for the exposure");
        Assert.AreEqual(6, renderer.Meters.Count, "the meter ignores the width");
    }

    [TestMethod]
    public void Exposure_ReExposesThePng_WithoutRendering()
    {
        var renderer = new FakeRenderer { SceneDependent = true };
        var session = new PreviewSession(renderer);
        Sidecar dark = session.Run(Base);
        byte[] png = File.ReadAllBytes(P("p.png"));
        byte[] exr = File.ReadAllBytes(P("p.exr"));
        Sidecar bright = session.Run(Camera(Base, "exposureEv", "2"));

        Assert.AreEqual(1, renderer.Scenes.Count);
        Assert.AreEqual(1, renderer.Meters.Count);
        Assert.AreEqual(dark.Exposure!.AppliedEv + 2.0, bright.Exposure!.AppliedEv, 1e-12);
        CollectionAssert.AreNotEqual(png, File.ReadAllBytes(P("p.png")), "the PNG is exposed anew");
        CollectionAssert.AreEqual(exr, File.ReadAllBytes(P("p.exr")), "the EXR is scene-referred");
        Assert.AreEqual(dark.Render!.Stats.RaysTraced, bright.Render!.Stats.RaysTraced, "the reused render's statistics");
        Assert.AreEqual(2.0, bright.Camera.ExposureEv);
    }

    [TestMethod]
    public void FewerDebugOutputs_ReuseTheRender_MoreRenderAgain()
    {
        var renderer = new FakeRenderer();
        var session = new PreviewSession(renderer);
        session.Run(Base with { Debug = new HashSet<DebugOutput> { DebugOutput.Depth, DebugOutput.Normal } });
        Sidecar fewer = session.Run(Base with { Debug = new HashSet<DebugOutput> { DebugOutput.Normal } });
        Assert.IsTrue(fewer.Stages.Reused.Contains(PreviewStage.Render));
        CollectionAssert.AreEquivalent(new[] { "beauty", "beautyExr", "normal", "sidecar" }, fewer.Outputs.Keys.ToArray());
        Sidecar other = session.Run(Base with { Debug = new HashSet<DebugOutput> { DebugOutput.Albedo } });
        Assert.IsTrue(other.Stages.Built.Contains(PreviewStage.Render), "albedo was not rendered last time");
        Assert.AreEqual(2, renderer.Scenes.Count);
        Assert.IsTrue(renderer.Requested!.SetEquals([Aov.Albedo]));
    }

    [TestMethod]
    public void Denoise_IsReused_UntilTheRenderChanges()
    {
        var denoiser = new FakeDenoiser();
        var session = new PreviewSession(new FakeRenderer(), denoiser);
        PreviewRequest request = Base with { Denoise = new DenoiseSettings() };
        Assert.IsTrue(session.Run(request).Stages.Built.Contains(PreviewStage.Denoise));
        byte[] denoised = File.ReadAllBytes(P("p.denoised.exr"));

        Sidecar exposed = session.Run(Camera(request, "exposureEv", "1"));
        Assert.IsTrue(exposed.Stages.Reused.Contains(PreviewStage.Denoise), "the input scale is the meter's, not the exposure's");
        Assert.AreEqual(1, denoiser.Inputs.Count);
        CollectionAssert.AreEqual(denoised, File.ReadAllBytes(P("p.denoised.exr")));
        Assert.IsNotNull(exposed.Denoise!.Run);

        Sidecar dusk = session.Run(Recipe(request, "time", "\"dusk\""));
        Assert.IsTrue(dusk.Stages.Built.Contains(PreviewStage.Denoise));
        Assert.AreEqual(2, denoiser.Inputs.Count);
        Sidecar cuda = session.Run(Recipe(request, "time", "\"dusk\"") with { Denoise = new DenoiseSettings { Device = DenoiseDevice.Cuda } });
        Assert.IsTrue(cuda.Stages.Built.Contains(PreviewStage.Denoise), "other settings, another denoised image");
        Assert.IsTrue(cuda.Stages.Reused.Contains(PreviewStage.Render));
    }

    [TestMethod]
    public void AFailedDenoise_IsTriedAgain()
    {
        var denoiser = new FakeDenoiser { Failure = "no library" };
        var session = new PreviewSession(new FakeRenderer(), denoiser);
        PreviewRequest request = Base with { Denoise = new DenoiseSettings() };
        session.Run(request);
        Sidecar again = session.Run(request);
        Assert.IsTrue(again.Stages.Built.Contains(PreviewStage.Denoise));
        Assert.AreEqual(2, denoiser.Inputs.Count);
    }

    [TestMethod]
    public void SunOnly_HasNoSkyDistribution()
    {
        Sidecar sidecar = PreviewPipeline.Run(Base with { LightSampling = LightSampling.SunOnly }, new FakeRenderer());
        Assert.IsFalse(sidecar.Stages.Built.Contains(PreviewStage.SkyDistribution));
        Assert.IsFalse(sidecar.Stages.Reused.Contains(PreviewStage.SkyDistribution));
    }

    [TestMethod]
    public void AFailingRender_LeavesTheSessionAsItWas()
    {
        var session = new PreviewSession(new FakeRenderer());
        session.Run(Base);
        Assert.ThrowsExactly<ArgumentException>(() => session.Run(Base with { Loupe = new PixelWindow(30, 0, 8, 6) }));
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => session.Run(Recipe(Base, "time", "\"teatime\"")));
        Sidecar same = session.Run(Base);
        Assert.AreEqual(0, same.Stages.Built.Count, "the failures changed nothing kept");
    }

    [TestMethod]
    public void RenderKey_CoversEveryFieldOfTheScene()
    {
        // A field added to VistaScene must join the render key, or a watch would reuse a render the renderer would now make differently.
        string[] key = [.. typeof(PreviewSession.RenderKey).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)];
        foreach (PropertyInfo field in typeof(VistaScene).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.IsTrue(key.Contains(field.Name) || PreviewSession.RenderKey.SceneFieldsNotRead.Contains(field.Name),
                $"VistaScene.{field.Name} is neither in PreviewSession.RenderKey nor among the fields the renderer does not read.");
        }
    }

    [TestMethod]
    public void Sidecar_RecordsTheStagesAndTheWatch()
    {
        var session = new PreviewSession(new FakeRenderer());
        session.Run(Base);
        Sidecar sidecar = session.Run(Camera(Base, "exposureEv", "1"), new WatchInfo(2, ["camera.exposureEv: 0 → 1"], 0.12));
        Assert.AreEqual(2, sidecar.Watch!.Render);
        Assert.IsTrue(sidecar.Watch.RunSeconds > 0.0, "the run is timed");
        Sidecar back = VistaJson.Deserialize<Sidecar>(File.ReadAllText(P("p.json")));
        CollectionAssert.AreEqual(sidecar.Stages.Reused.ToArray(), back.Stages.Reused.ToArray());
        Assert.AreEqual(0, back.Stages.Built.Count);
        Assert.AreEqual("camera.exposureEv: 0 → 1", back.Watch!.Changes.Single());
        StringAssert.Contains(File.ReadAllText(P("p.json")), "\"accelerationStructure\"");
        Assert.AreEqual(9, back.Schema);
    }
}
