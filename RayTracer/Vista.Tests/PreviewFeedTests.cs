using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.Sliders;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// What M1.16's window needs of the session (vista-plan §10 M1.16): a feed that stops a run between stages and dispatches and shows its
/// image after each pass without changing a byte of its files; two meshes kept, the preview's and the final's; an estimate that leaves the
/// final's stages in place and counts only what the final will still build; and an implausible hint's sliders.
/// </summary>
[TestClass]
public sealed class PreviewFeedTests
{
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
        Width = 64,
        Height = 36,
        SamplesPerPixel = 16,
        Grid = TestWorlds.Grid,
        Erosion = false,
    };

    [TestMethod]
    public void Planner_RefusesItsNextBatch_OnceCancelled()
    {
        using var stop = new CancellationTokenSource();
        var planner = new BatchPlanner(10, 10, 4) { Feed = new TraceFeed { Cancellation = stop.Token } };
        SampleBatch first = planner.Next();
        planner.Complete(first, TimeSpan.FromMilliseconds(1));
        stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => planner.Next());
    }

    [TestMethod]
    public void Planner_SaysWhichBatchEndsAPass()
    {
        var planner = new BatchPlanner(10, 100, 2, new BatchPlannerOptions { DispatchBudgetMs = 0.001, InitialCostNs = 100.0 });
        var ends = new List<bool>();
        while (planner.HasNext)
        {
            SampleBatch batch = planner.Next();
            ends.Add(planner.EndsPass(batch));
            planner.Complete(batch, TimeSpan.FromTicks(1));
        }
        Assert.IsTrue(ends.Count > 2, "row bands");
        Assert.AreEqual(2, ends.Count(e => e), "two passes of one sample, each ending once");
        Assert.IsTrue(ends[^1]);
    }

    [TestMethod]
    public void Feed_ShowsEachPass_AndChangesNoFile()
    {
        // The fake's batches take 1 ms against a 0.05 ms budget: one sample a pass, so 16 passes and 15 frames before the image.
        PreviewRequest request = Base with { DispatchBudgetMs = 0.05 };
        PreviewPipeline.Run(request with { OutPath = P("cold.png") }, new FakeRenderer());

        var frames = new List<PreviewFrame>();
        var renderer = new FakeRenderer();
        var session = new PreviewSession(renderer);
        Sidecar fed = session.Run(request, feed: new PreviewFeed { Frame = frames.Add, IntervalSeconds = 0.0 });

        Assert.AreEqual(15, frames.Count, "a frame after every pass but the last");
        CollectionAssert.AreEqual(Enumerable.Range(1, 15).ToArray(), frames.Select(f => f.Samples).ToArray());
        Assert.IsTrue(frames.All(f => f.Width == 64 && f.Height == 36 && f.SamplesPerPixel == 16 && f.Rgb.Length == 64 * 36 * 3));
        Assert.AreEqual(1, renderer.Incidents.Count, "metered once, before the image, so the frames are exposed as the PNG");
        foreach (string suffix in new[] { ".png", ".exr" })
            CollectionAssert.AreEqual(File.ReadAllBytes(P("cold" + suffix)), File.ReadAllBytes(P("p" + suffix)), $"the fed run's {suffix} is a cold run's");
        Assert.IsTrue(fed.Stages.Built.Contains(PreviewStage.ExposureMeter));
    }

    [TestMethod]
    public void Feed_StopsARun_AndTheSessionCarriesOn()
    {
        var renderer = new FakeRenderer();
        var session = new PreviewSession(renderer);
        using var stop = new CancellationTokenSource();
        renderer.Dispatched = _ => stop.Cancel();
        PreviewRequest request = Base with { DispatchBudgetMs = 0.05 };
        Assert.ThrowsExactly<OperationCanceledException>(() => session.Run(request, feed: new PreviewFeed { Cancellation = stop.Token }));
        Assert.IsFalse(File.Exists(P("p.json")), "a stopped run writes no sidecar");
        Assert.IsNull(session.LastRender, "nor keeps a half render");
        Assert.IsNotNull(session.LastWorld, "but keeps the world it built");

        renderer.Dispatched = null;
        Sidecar again = session.Run(request);
        Assert.IsTrue(again.Stages.Reused.Contains(PreviewStage.World) && again.Stages.Reused.Contains(PreviewStage.Mesh));
        Assert.IsTrue(again.Stages.Built.Contains(PreviewStage.Render));
        byte[] png = File.ReadAllBytes(P("p.png"));
        PreviewPipeline.Run(request with { OutPath = P("cold.png") }, new FakeRenderer());
        CollectionAssert.AreEqual(File.ReadAllBytes(P("cold.png")), png, "the run after a stop is a cold run's");
    }

    [TestMethod]
    public void Feed_StoppedAtOnce_RunsNothing()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var renderer = new FakeRenderer();
        Assert.ThrowsExactly<OperationCanceledException>(() => new PreviewSession(renderer).Run(Base, feed: new PreviewFeed { Cancellation = stop.Token }));
        Assert.AreEqual(0, renderer.Renders.Count);
    }

    [TestMethod]
    public void WorldCache_KeepsTwoMeshes()
    {
        var cache = new WorldCache();
        Recipe recipe = VistaJson.ParseRecipe(File.ReadAllText(P("r.json")));
        TerrainWorld world = cache.World(recipe, 7, TestWorlds.Small);
        var settings = new MeshSettings();
        MeshView View(double x) => MeshView.For(DefaultCamera.For(world.Surface) with { Position = new WorldPoint(x, 400, 0) }, 64, 36, 6_371_000.0);

        TerrainMesh a = cache.Mesh(View(0), settings);
        TerrainMesh b = cache.Mesh(View(500), settings);
        Assert.AreSame(a, cache.Mesh(View(0), settings, out bool reused), "the preview's mesh survives the final's");
        Assert.IsTrue(reused);
        Assert.AreSame(b, cache.Mesh(View(500), settings));
        TerrainMesh c = cache.Mesh(View(1000), settings);
        Assert.AreNotSame(a, cache.Mesh(View(0), settings), "a third mesh lets the least recent go");
        Assert.AreNotSame(c, b);
    }

    [TestMethod]
    public void WindowEstimate_RendersThePreviewFirst_AndCountsWhatTheFinalStillBuilds()
    {
        var renderer = new FakeRenderer();
        var session = new PreviewSession(renderer) { KeepsFinalStages = true };
        PreviewRequest preview = Base with { Width = 32, Height = 18, SamplesPerPixel = 64 };
        session.Run(preview);
        // A final larger than the estimate's preview (320 wide), so the two have meshes of their own.
        PreviewRequest final = Base with { Width = 400, Height = 225 };
        Sidecar estimated = session.Run(final with { Estimate = true });

        Assert.AreEqual(3, renderer.Renders.Count, "the window's preview, then the estimate's preview, then its probe");
        Assert.AreEqual(320, renderer.Renders[1].Settings.Width, "the estimate's preview at its own size");
        Assert.IsTrue(renderer.Renders[1].Aovs.SetEquals([Aov.Variance]));
        Assert.AreEqual(FinalEstimate.ProbeGpuSeconds, renderer.Renders[2].Settings.Batching.StopAfterGpuSeconds, "the probe last");
        EstimateInfo e = estimated.Estimate!;
        foreach (string kept in new[] { "mesh", "worldGen", "exposureMeter", "render.meshUpload", "render.accelBuild" })
            Assert.IsFalse(e.SetupSeconds.ContainsKey(kept), $"{kept} is the final's already");
        Assert.IsTrue(e.SetupSeconds.ContainsKey("pools") && e.SetupSeconds.ContainsKey("memoryPlan"), "what every final runs stays");
        Assert.AreEqual(64, e.Noise!.PreviewSamples);

        Sidecar rendered = session.Run(final);
        Assert.IsTrue(rendered.Stages.Reused.Contains(PreviewStage.Mesh) && rendered.Stages.Reused.Contains(PreviewStage.AccelerationStructure),
            "the final takes the probe's mesh and acceleration structure");
        Assert.IsTrue(rendered.Stages.Built.Contains(PreviewStage.Render));
    }

    [TestMethod]
    public void CliEstimate_ProbesFirst_AndCountsEverything()
    {
        var renderer = new FakeRenderer();
        Sidecar estimated = new PreviewSession(renderer).Run(Base with { Estimate = true });
        Assert.AreEqual(FinalEstimate.ProbeGpuSeconds, renderer.Renders[0].Settings.Batching.StopAfterGpuSeconds, "the probe first, as M1.15 measured it");
        Assert.IsTrue(estimated.Estimate!.SetupSeconds.ContainsKey("mesh") && estimated.Estimate.SetupSeconds.ContainsKey("memoryPlan"));
    }

    [TestMethod]
    public void StillToBuild_DropsTheKeptStages()
    {
        var setup = new Dictionary<string, double>
        {
            ["startup"] = 1, ["worldGen"] = 2, ["mesh"] = 3, ["pools"] = 4, ["memoryPlan"] = 5, ["exposureMeter"] = 6,
            ["render.deviceInit"] = 7, ["render.meshUpload"] = 8, ["render.accelBuild"] = 9, ["render.atmosphereLuts"] = 10,
            ["render.setup"] = 11, ["render.readback"] = 12, ["render.lowAirShaderCompile"] = 13,
        };
        CollectionAssert.AreEqual(new[] { "memoryPlan", "pools", "render.readback", "render.setup" }, FinalEstimate.StillToBuild(setup).Keys.ToArray());
    }

    [TestMethod]
    public void ImplausibleSliders_NameThePinsAHintIsAbout()
    {
        var recipe = new Recipe { SetPieces = [SetPiece.AlpineRange] };
        SliderState plain = LinkedSliders.Resolve(recipe, new SliderPins { Season = Season.Summer });
        Assert.AreEqual(0, plain.ImplausibleSliders.Count);

        SliderState mismatch = LinkedSliders.Resolve(recipe, new SliderPins { Season = Season.Summer, Date = new DateOnly(2026, 1, 15) });
        Assert.IsTrue(mismatch.ImplausibleSliders.SetEquals([Slider.Date, Slider.Season]));
        Assert.AreEqual(1, mismatch.Implausible.Count);

        var frozen = new Recipe { SetPieces = [SetPiece.FrozenLake] };
        SliderState warm = LinkedSliders.Resolve(frozen, new SliderPins { Temperature = 25 });
        Assert.IsTrue(warm.ImplausibleSliders.Contains(Slider.Temperature));
        Assert.IsFalse(warm.ImplausibleSliders.Contains(Slider.Season), "the season is auto: the hint is about the pin");
    }
}
