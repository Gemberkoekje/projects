using System.Collections.Concurrent;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.Sliders;
using Vista.Tweak;

namespace Vista.Tests;

/// <summary>
/// The window's worker (vista-plan §10 M1.16): every job ends once; the newest preview runs and stops a stale one; the auto camera,
/// estimate and final queue ahead of previews and only a cancel stops them; a missing engine fails every job with its reason; and a window
/// run writes what the CLI writes for the same files.
/// </summary>
[TestClass]
public sealed class TweakRunnerTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);
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

    private TweakPaths Paths => TweakPaths.Of(_dir, "scene");

    private static TweakScene Scene => new()
    {
        Recipe = new Recipe { SetPieces = [SetPiece.AlpineRange] },
        Seed = 7,
        Camera = new CameraSpec { Position = new WorldPoint(0, 3000, -9000), PitchDegrees = 3 },
        Pins = new SliderPins { Time = TimeOfDay.Day, Location = Astronomy.Place.Zermatt, Date = new DateOnly(2026, 7, 15) },
        Preview = new TweakPreview { Width = 32, SamplesPerPixel = 4 },
        Final = new TweakFinal { Width = 64, SamplesPerPixel = 4 },
    };

    /// <summary>The CLI's request for the test's small world (the window's arguments make the default world, too big for CI).</summary>
    private static PreviewRequest Small(PreviewRequest request) => request with { Grid = TestWorlds.Grid, Erosion = false, DispatchBudgetMs = 0.05 };

    private static TweakRunner Runner(FakeRenderer renderer, Action<TraceProgress>? progress = null) =>
        new(p => new TweakEngine(new PreviewSession(renderer) { KeepsFinalStages = true, Progress = progress ?? p }, []))
        {
            Adjust = Small,
            FrameIntervalSeconds = 0.0,
        };

    private static List<TweakResult> Collect(TweakRunner runner)
    {
        var results = new List<TweakResult>();
        runner.Finished += r =>
        {
            lock (results)
                results.Add(r);
        };
        return results;
    }

    [TestMethod]
    public void Preview_WritesTheFilesAndShowsItsImage()
    {
        var renderer = new FakeRenderer();
        using TweakRunner runner = Runner(renderer);
        List<TweakResult> results = Collect(runner);
        var frames = new ConcurrentBag<PreviewFrame>();
        runner.Frame += (_, f) => frames.Add(f);
        TweakJob job = runner.Submit(TweakRun.Preview, Scene, Paths, P("preview.png"));
        Assert.IsTrue(runner.WaitIdle(Wait));

        TweakResult result = results.Single();
        Assert.AreSame(job, result.Job);
        Assert.IsTrue(result.Succeeded, result.Problem);
        Assert.IsTrue(File.Exists(Paths.Recipe) && File.Exists(Paths.Camera) && File.Exists(Paths.Sliders) && File.Exists(Paths.Options));
        Assert.AreEqual("beauty", result.ImageRole);
        Assert.AreEqual((32, 18), (result.Image!.Width, result.Image.Height));
        Assert.AreEqual(3, frames.Count, "a frame after each of the passes but the last");
        Assert.IsNotNull(result.World);
    }

    [TestMethod]
    public void WindowRun_IsTheCliRunOfItsFiles()
    {
        using (TweakRunner runner = Runner(new FakeRenderer()))
        {
            List<TweakResult> results = Collect(runner);
            runner.Submit(TweakRun.Preview, Scene with { Preview = Scene.Preview with { View = TweakView.Depth } }, Paths, P("window.png"));
            Assert.IsTrue(runner.WaitIdle(Wait));
            Assert.AreEqual("depth", results.Single().ImageRole);
        }
        IReadOnlyList<string> args = TweakFiles.Arguments(TweakFiles.Read(Paths.Recipe, out _) with { Preview = Scene.Preview with { View = TweakView.Depth } },
            Paths, TweakRun.Preview, P("cli.png"));
        PreviewPipeline.Run(Small(TweakFiles.Request(args)), new FakeRenderer());
        foreach (string suffix in new[] { ".png", ".exr", ".depth.png" })
            CollectionAssert.AreEqual(File.ReadAllBytes(P("cli" + suffix)), File.ReadAllBytes(P("window" + suffix)), $"{suffix} byte for byte");
    }

    [TestMethod]
    public void NewerPreviews_ReplaceWaitingOnes_AndStopARunningOne()
    {
        var renderer = new FakeRenderer();
        using var started = new ManualResetEventSlim();
        using var go = new ManualResetEventSlim();
        renderer.Dispatched = _ =>
        {
            started.Set();
            go.Wait(Wait);
        };
        using TweakRunner runner = Runner(renderer);
        List<TweakResult> results = Collect(runner);
        TweakJob first = runner.Submit(TweakRun.Preview, Scene, Paths, P("p.png"));
        Assert.IsTrue(started.Wait(Wait), "the first preview is tracing");
        TweakJob second = runner.Submit(TweakRun.Preview, Scene with { Seed = 8 }, Paths, P("p.png"));
        TweakJob third = runner.Submit(TweakRun.Preview, Scene with { Seed = 9 }, Paths, P("p.png"));
        go.Set();
        Assert.IsTrue(runner.WaitIdle(Wait));

        Assert.AreEqual(3, results.Count, "every job ends once");
        Assert.IsTrue(results.Single(r => r.Job == first).Cancelled, "the running preview was stopped");
        Assert.IsTrue(results.Single(r => r.Job == second).Cancelled, "the waiting one was dropped");
        TweakResult last = results.Single(r => r.Job == third);
        Assert.IsTrue(last.Succeeded, last.Problem);
        Assert.AreEqual(9UL, last.Sidecar!.Seed);
    }

    [TestMethod]
    public void ExplicitJobs_GoFirst_AndOnlyACancelStopsThem()
    {
        var renderer = new FakeRenderer();
        using var started = new ManualResetEventSlim();
        using var go = new ManualResetEventSlim();
        renderer.Dispatched = _ =>
        {
            started.Set();
            go.Wait(Wait);
        };
        using TweakRunner runner = Runner(renderer);
        List<TweakResult> results = Collect(runner);
        TweakJob estimate = runner.Submit(TweakRun.Estimate, Scene, Paths, P("final.png"));
        Assert.IsTrue(started.Wait(Wait));
        TweakJob preview = runner.Submit(TweakRun.Preview, Scene, Paths, P("p.png"));
        TweakJob final = runner.Submit(TweakRun.Final, Scene, Paths, P("final.png"));
        TweakJob dropped = runner.Submit(TweakRun.Final, Scene, Paths, P("other.png"));
        runner.Cancel(dropped);
        go.Set();
        Assert.IsTrue(runner.WaitIdle(Wait));

        CollectionAssert.AreEqual(new[] { dropped, estimate, final, preview }, results.Select(r => r.Job).ToArray(),
            "the cancelled final ended at once, the estimate ran on, the final went before the preview");
        Assert.IsTrue(results[0].Cancelled);
        Assert.IsTrue(results[1].Succeeded && results[1].Sidecar!.Estimate is not null, "a preview never stops an estimate");
        Assert.IsTrue(results[2].Succeeded && results[3].Succeeded);
        Assert.IsTrue(results[2].Sidecar!.Stages.Reused.Contains(PreviewStage.AccelerationStructure), "the final on the estimate's kept stages");
        Assert.IsNull(results[2].Image, "a final shows no preview image");
    }

    [TestMethod]
    public void Cancel_StopsTheRunningJob()
    {
        var renderer = new FakeRenderer();
        using TweakRunner runner = Runner(renderer);
        List<TweakResult> results = Collect(runner);
        TweakJob? running = null;
        runner.Started += job => running = job;
        renderer.Dispatched = _ => runner.Cancel(running!);
        runner.Submit(TweakRun.Final, Scene, Paths, P("final.png"));
        Assert.IsTrue(runner.WaitIdle(Wait));
        Assert.IsTrue(results.Single().Cancelled);
        Assert.IsFalse(File.Exists(P("final.json")));
    }

    [TestMethod]
    public void Final_ReportsItsProgress()
    {
        var renderer = new FakeRenderer();
        var progress = new ConcurrentBag<TraceProgress>();
        using TweakRunner runner = Runner(renderer);
        runner.Progress += (job, p) =>
        {
            Assert.AreEqual(TweakRun.Final, job.Kind);
            progress.Add(p);
        };
        runner.Submit(TweakRun.Preview, Scene, Paths, P("p.png"));
        runner.Submit(TweakRun.Final, Scene, Paths, P("final.png"));
        Assert.IsTrue(runner.WaitIdle(Wait));
        Assert.IsTrue(progress.Count > 0, "the final's trace reports its progress");
    }

    [TestMethod]
    public void NoEngine_FailsEveryJob_WithTheReason()
    {
        string? broken = null;
        using var runner = new TweakRunner(_ => throw new PlatformNotSupportedException("no DXR here"));
        runner.Broken += m => broken = m;
        List<TweakResult> results = Collect(runner);
        runner.Submit(TweakRun.Preview, Scene, Paths, P("p.png"));
        Assert.IsTrue(runner.WaitIdle(Wait));
        runner.Submit(TweakRun.Final, Scene, Paths, P("f.png"));
        Assert.AreEqual("no DXR here", broken);
        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.All(r => r.Problem == "no DXR here"));
    }

    [TestMethod]
    public void AFailedRun_SaysWhy_AndTheRunnerCarriesOn()
    {
        var renderer = new FakeRenderer();
        using TweakRunner runner = Runner(renderer);
        List<TweakResult> results = Collect(runner);
        TweakScene broken = Scene with { Pins = Scene.Pins with { Location = Astronomy.Place.Zermatt, Latitude = 10 } };
        runner.Submit(TweakRun.Preview, broken, Paths, P("p.png"));
        Assert.IsTrue(runner.WaitIdle(Wait));
        runner.Submit(TweakRun.Preview, Scene, Paths, P("p.png"));
        Assert.IsTrue(runner.WaitIdle(Wait));
        StringAssert.Contains(results[0].Problem, "not both");
        Assert.IsTrue(results[1].Succeeded);
    }
}
