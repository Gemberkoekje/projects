using Vista.Cli;
using Vista.Output;

namespace Vista.Tests;

/// <summary>
/// Watch mode's files (vista-plan §10 M1.8): the first poll renders, a change renders once it has settled, a save that changes
/// nothing renders nothing, the options file changes the seed, and a broken, missing or forbidden input is reported while the watch
/// goes on. Real files, a fake renderer and a clock the test moves.
/// </summary>
[TestClass]
public sealed class PreviewWatchTests
{
    private const string CameraJson = """{ "position": { "x": 0, "y": 3000, "z": -5000 }, "pitchDegrees": -5, "exposureEv": 0 }""";

    private string _dir = null!;
    private ManualTime _time = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(P("r.json"), """{ "setPieces": ["alpineRange"], "time": "day" }""");
        File.WriteAllText(P("c.json"), CameraJson);
        File.WriteAllText(P("o.txt"), "# the seed lives here\n--seed 7\n");
        _time = new ManualTime();
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    private string P(string name) => Path.Combine(_dir, name);

    private (PreviewWatch Watch, FakeRenderer Renderer) Watch(params string[] extra)
    {
        string[] args =
        [
            "--watch", "--recipe", P("r.json"), "--camera", P("c.json"), "--out", P("p.png"), "--width", "32",
            "--grid", "65x512", "--erosion", "off", "--options", P("o.txt"), .. extra,
        ];
        Assert.IsTrue(WatchCommandLine.TryParse(args, out WatchRequest? request, out IReadOnlyList<string> errors), string.Join(" ", errors));
        var renderer = new FakeRenderer();
        return (new PreviewWatch(request!, new PreviewSession(renderer, new FakeDenoiser()), _time), renderer);
    }

    /// <summary>Polls as the loop does, every <see cref="PreviewWatch.PollInterval"/>, until something happens or a second passes.</summary>
    private WatchEvent Next(PreviewWatch watch)
    {
        for (int i = 0; i < 20; i++)
        {
            WatchEvent step = watch.Poll();
            if (step.Kind != WatchEventKind.Idle)
                return step;
            _time.Advance(PreviewWatch.PollInterval);
        }
        return new WatchEvent(WatchEventKind.Idle);
    }

    [TestMethod]
    public void FirstPoll_RendersAtOnce_ThenIdles()
    {
        (PreviewWatch watch, FakeRenderer renderer) = Watch();
        WatchEvent first = watch.Poll();
        Assert.AreEqual(WatchEventKind.Rendered, first.Kind, first.Message);
        Assert.AreEqual(1, first.Sidecar!.Watch!.Render);
        Assert.AreEqual(0, first.Sidecar.Watch.Changes.Count);
        Assert.AreEqual(0.0, first.Sidecar.Watch.WaitSeconds);
        Assert.AreEqual(7ul, first.Sidecar.Seed, "the seed came from the options file");
        CollectionAssert.AreEqual(new[] { P("o.txt"), P("r.json"), P("c.json") }, watch.Watched.ToArray());
        Assert.AreEqual(WatchEventKind.Idle, Next(watch).Kind);
        Assert.AreEqual(1, renderer.Scenes.Count);
    }

    [TestMethod]
    public void AChange_RendersOnceItHasSettled()
    {
        (PreviewWatch watch, FakeRenderer renderer) = Watch();
        watch.Poll();
        File.WriteAllText(P("c.json"), CameraJson.Replace("\"exposureEv\": 0", "\"exposureEv\": 1", StringComparison.Ordinal));
        Assert.AreEqual(WatchEventKind.Idle, watch.Poll().Kind, "seen, not settled");
        _time.Advance(PreviewWatch.Settle / 2);
        Assert.AreEqual(WatchEventKind.Idle, watch.Poll().Kind, "still settling");
        _time.Advance(PreviewWatch.Settle);
        WatchEvent step = watch.Poll();
        Assert.AreEqual(WatchEventKind.Rendered, step.Kind, step.Message);
        Assert.AreEqual(2, step.Sidecar!.Watch!.Render);
        CollectionAssert.AreEqual(new[] { "camera.exposureEv: 0 → 1" }, step.Sidecar.Watch.Changes.ToArray());
        Assert.AreEqual(1.5 * PreviewWatch.Settle.TotalSeconds, step.Sidecar.Watch.WaitSeconds, 1e-9);
        Assert.AreEqual(0, step.Sidecar.Stages.Built.Count, "the exposure is display only");
        Assert.AreEqual(1, renderer.Scenes.Count);
        Assert.AreEqual(WatchEventKind.Idle, Next(watch).Kind);
        Assert.AreEqual(2, watch.Renders);
    }

    [TestMethod]
    public void EditsInARow_AreOneRender()
    {
        (PreviewWatch watch, FakeRenderer renderer) = Watch();
        watch.Poll();
        File.WriteAllText(P("r.json"), """{ "setPieces": ["alpineRange"], "time": "dusk" }""");
        watch.Poll();
        _time.Advance(PreviewWatch.PollInterval);
        File.WriteAllText(P("c.json"), CameraJson.Replace("\"pitchDegrees\": -5", "\"pitchDegrees\": -8", StringComparison.Ordinal));
        WatchEvent step = Next(watch);
        Assert.AreEqual(WatchEventKind.Rendered, step.Kind, step.Message);
        CollectionAssert.AreEqual(new[] { "recipe.time: day → dusk", "camera.pitchDegrees: -5 → -8" }, step.Sidecar!.Watch!.Changes.ToArray());
        Assert.AreEqual(2, renderer.Scenes.Count);
    }

    [TestMethod]
    public void SavingUnchanged_RendersNothing()
    {
        (PreviewWatch watch, FakeRenderer renderer) = Watch();
        watch.Poll();
        File.WriteAllText(P("c.json"), CameraJson);
        File.SetLastWriteTimeUtc(P("c.json"), DateTime.UtcNow.AddMinutes(1));
        Assert.AreEqual(WatchEventKind.Idle, Next(watch).Kind);
        Assert.AreEqual(1, watch.Renders);
        Assert.AreEqual(1, renderer.Scenes.Count);
    }

    [TestMethod]
    public void TheOptionsFile_ChangesTheSeed_AndOverridesTheCommandLine()
    {
        (PreviewWatch watch, _) = Watch("--spp", "16");
        Assert.AreEqual(16, watch.Poll().Sidecar!.Image.SamplesPerPixel);
        File.WriteAllText(P("o.txt"), "--seed 8 --spp 4   # and fewer samples\n--time dusk\n");
        WatchEvent step = Next(watch);
        Assert.AreEqual(WatchEventKind.Rendered, step.Kind, step.Message);
        Assert.AreEqual(8ul, step.Sidecar!.Seed);
        Assert.AreEqual(4, step.Sidecar.Image.SamplesPerPixel);
        Assert.AreEqual("dusk", step.Sidecar.Sliders.Values["time"].Value, "the options file pins the time of day");
        CollectionAssert.AreEqual(new[] { "--seed: 7 → 8", "--spp: 16 → 4", "--time: (none) → dusk" }, step.Sidecar.Watch!.Changes.ToArray());
        Assert.IsTrue(step.Sidecar.Stages.Built.Contains(PreviewStage.World));

        // A line taken out of the file gives the command line's value back.
        File.WriteAllText(P("o.txt"), "--seed 8\n");
        Assert.AreEqual(16, Next(watch).Sidecar!.Image.SamplesPerPixel);
    }

    [TestMethod]
    public void ABrokenFile_IsReportedOnce_AndTheWatchGoesOn()
    {
        (PreviewWatch watch, FakeRenderer renderer) = Watch();
        watch.Poll();
        File.WriteAllText(P("c.json"), CameraJson.Replace("}", "", StringComparison.Ordinal));
        WatchEvent broken = Next(watch);
        Assert.AreEqual(WatchEventKind.Failed, broken.Kind);
        StringAssert.StartsWith(broken.Message, P("c.json") + ": ", "the message names the broken file");
        Assert.AreEqual(WatchEventKind.Idle, Next(watch).Kind, "reported once");

        File.WriteAllText(P("c.json"), CameraJson.Replace("\"pitchDegrees\": -5", "\"pitchDegrees\": -6", StringComparison.Ordinal));
        WatchEvent fixedStep = Next(watch);
        Assert.AreEqual(WatchEventKind.Rendered, fixedStep.Kind, fixedStep.Message);
        Assert.AreEqual(2, fixedStep.Sidecar!.Watch!.Render, "renders count, failures do not");
        CollectionAssert.AreEqual(new[] { "camera.pitchDegrees: -5 → -6" }, fixedStep.Sidecar.Watch.Changes.ToArray(), "against the last image");
        Assert.AreEqual(2, renderer.Scenes.Count);
    }

    [TestMethod]
    public void BadOrForbiddenOptions_AreReported()
    {
        (PreviewWatch watch, _) = Watch();
        watch.Poll();
        foreach ((string options, string expected) in new[]
        {
            ("--seed 7 --spp zero", "--spp"),
            ("--seed 7 --dry-run", "--dry-run cannot change"),
            ("--seed 7 --options other.txt", "cannot name another"),
            ("--seed 7 --out \"unclosed", "not closed"),
            ("--seed 7 --loupe 30,0,8,6", "does not fit"),
        })
        {
            File.WriteAllText(P("o.txt"), options);
            WatchEvent step = Next(watch);
            Assert.AreEqual(WatchEventKind.Failed, step.Kind, options);
            StringAssert.Contains(step.Message, expected, options);
        }
    }

    [TestMethod]
    public void AMissingFile_IsWaitedFor()
    {
        (PreviewWatch watch, _) = Watch();
        watch.Poll();
        File.Delete(P("c.json"));
        Assert.AreEqual(WatchEventKind.Idle, Next(watch).Kind, "a moment's absence is an editor saving");
        _time.Advance(PreviewWatch.MissingGrace);
        WatchEvent waiting = watch.Poll();
        Assert.AreEqual(WatchEventKind.Waiting, waiting.Kind);
        StringAssert.Contains(waiting.Message, "c.json");
        Assert.AreEqual(WatchEventKind.Idle, Next(watch).Kind, "said once");

        File.WriteAllText(P("c.json"), CameraJson);
        Assert.AreEqual(WatchEventKind.Idle, Next(watch).Kind, "back as it was: nothing to render");
        File.WriteAllText(P("c.json"), CameraJson.Replace("\"exposureEv\": 0", "\"exposureEv\": -1", StringComparison.Ordinal));
        Assert.AreEqual(WatchEventKind.Rendered, Next(watch).Kind);
    }

    [TestMethod]
    public void AnotherCamera_IsWatchedInstead()
    {
        (PreviewWatch watch, _) = Watch();
        watch.Poll();
        File.WriteAllText(P("d.json"), CameraJson.Replace("\"y\": 3000", "\"y\": 3500", StringComparison.Ordinal));
        File.WriteAllText(P("o.txt"), $"--seed 7 --camera \"{P("d.json")}\"");
        WatchEvent step = Next(watch);
        Assert.AreEqual(WatchEventKind.Rendered, step.Kind, step.Message);
        Assert.AreEqual(3500.0, step.Sidecar!.Camera.Position.Y);
        CollectionAssert.Contains(watch.Watched.ToArray(), P("d.json"));
        CollectionAssert.DoesNotContain(watch.Watched.ToArray(), P("c.json"));
        StringAssert.Contains(step.Sidecar.Watch!.Changes.Single(), "--camera: ");
    }

    [TestMethod]
    public void Run_ReportsUntilStopped()
    {
        (PreviewWatch watch, _) = Watch();
        using var stop = new CancellationTokenSource();
        var events = new List<WatchEvent>();
        watch.Run(step =>
        {
            events.Add(step);
            stop.Cancel();
        }, stop.Token);
        Assert.AreEqual(WatchEventKind.Rendered, events.Single().Kind);
    }

    [TestMethod]
    public void ARenderingWatch_NeedsARenderer()
    {
        Assert.IsTrue(WatchCommandLine.TryParse(["--watch", "--recipe", P("r.json"), "--seed", "1", "--out", P("p.png")], out WatchRequest? request, out _));
        Assert.ThrowsExactly<ArgumentException>(() => new PreviewWatch(request!, new PreviewSession(renderer: null)));
        Assert.IsTrue(WatchCommandLine.TryParse(["--watch", "--recipe", P("r.json"), "--seed", "1", "--out", P("p.png"), "--dry-run",
            "--grid", "65x512", "--erosion", "off"], out WatchRequest? dry, out _));
        WatchEvent step = new PreviewWatch(dry!, new PreviewSession(renderer: null), _time).Poll();
        Assert.AreEqual(WatchEventKind.Rendered, step.Kind, step.Message);
        Assert.AreEqual("dryRun", step.Sidecar!.Kind);
    }

    [TestMethod]
    public void JsonChanges_NameEachField()
    {
        CollectionAssert.AreEqual(
            new[] { "camera.position.y: 1 → 2", "camera.fStop: 0 → (none)", "camera.yawDegrees: (none) → 5" },
            PreviewWatch.JsonChanges("camera", """{ "position": { "x": 0, "y": 1 }, "fStop": 0 }""", """{ "position": { "x": 0, "y": 2 }, "yawDegrees": 5 }""").ToArray());
        CollectionAssert.AreEqual(new[] { """recipe.setPieces: ["alpineRange"] → ["alpineRange","lake"]""" },
            PreviewWatch.JsonChanges("recipe", """{ "setPieces": ["alpineRange"] }""", """{ "setPieces": ["alpineRange", "lake"] }""").ToArray());
        CollectionAssert.AreEqual(new[] { "recipe: reformatted, no field changed" },
            PreviewWatch.JsonChanges("recipe", """{ "time": "day" }""", "{\n  \"time\": \"day\"\n}\n").ToArray());
        CollectionAssert.AreEqual(new[] { "recipe: not valid JSON" }, PreviewWatch.JsonChanges("recipe", "{}", "{").ToArray());
    }

    /// <summary>A clock the test moves.</summary>
    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
