using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// The estimate and the final's own clock and memory in a session (vista-plan §10 M1.15), on the fake renderer: an estimate writes its
/// sidecar only, probes the final's size with the final's outputs, renders the preview for its noise and plans the memory before
/// anything else; a final that does not fit is refused before it renders, a tight one warned about at once; every image render records
/// its memory's plan and peak and its trace's checkpoints, and reports its progress.
/// </summary>
[TestClass]
public sealed class EstimateSessionTests
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
    public void Estimate_WritesItsSidecarOnly()
    {
        var renderer = new FakeRenderer();
        Sidecar sidecar = PreviewPipeline.Run(Base with { Estimate = true, Debug = new HashSet<DebugOutput> { DebugOutput.Depth } }, renderer);

        CollectionAssert.AreEquivalent(new[] { "p.estimate.json", "r.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName).ToArray(),
            "the probe's files are removed, and no final file is written");
        Assert.AreEqual("estimate", sidecar.Kind);
        Assert.IsNull(sidecar.Render);
        CollectionAssert.AreEqual(new[] { "sidecar" }, sidecar.Outputs.Keys.ToArray());
        Assert.AreEqual("p.estimate.json", sidecar.Outputs["sidecar"]);

        EstimateInfo e = sidecar.Estimate!;
        Assert.AreEqual((64, 36, 16), (e.Width, e.Height, e.SamplesPerPixel));
        Assert.AreEqual(16, e.Probe!.SamplesPerPixel, "the fake's 1 ms a batch never reaches the probe's stop: the probe traces it all");
        Assert.IsTrue(e.Probe.Passes.Count >= 1);
        Assert.IsTrue(e.Total!.Value.Low <= e.Total.Value.Mid && e.Total.Value.Mid <= e.Total.Value.High);
        Assert.AreEqual(e.Setup.Mid + e.Trace!.Value.Mid + e.Write!.Value.Mid, e.Total.Value.Mid, 1e-9);
        Assert.IsTrue(e.SetupSeconds.ContainsKey("mesh") && e.SetupSeconds.ContainsKey("worldGen"));
        Assert.IsFalse(e.SetupSeconds.ContainsKey("render"), "the probe's render is projected, not counted");
        Assert.IsNull(e.Denoise);
        Assert.IsTrue(e.Memory.Fits);
        Assert.AreEqual(FakeRenderer.PlanBaseBytes, e.Memory.BaseBytes);

        // The probe at the final's size with the final's outputs; then the preview at its own size (at most 320 wide, at least 64
        // samples), with the variance alone.
        Assert.AreEqual(2, renderer.Renders.Count, "the probe and the preview");
        (RenderSettings probe, IReadOnlySet<Aov> probeAovs) = renderer.Renders[0];
        Assert.AreEqual((64, 36, 16), (probe.Width, probe.Height, probe.SamplesPerPixel));
        Assert.AreEqual(FinalEstimate.ProbeGpuSeconds, probe.Batching.StopAfterGpuSeconds);
        Assert.IsTrue(probeAovs.SetEquals([Aov.Depth]));
        (RenderSettings preview, IReadOnlySet<Aov> previewAovs) = renderer.Renders[1];
        Assert.AreEqual((64, 36, 64), (preview.Width, preview.Height, preview.SamplesPerPixel));
        Assert.IsTrue(previewAovs.SetEquals([Aov.Variance]));
        Assert.AreEqual(64, e.Noise!.PreviewSamples);
        Assert.IsNull(e.Noise.SamplesForTarget);
    }

    [TestMethod]
    public void Estimate_WithATarget_SaysTheSamplesAndTheirTime()
    {
        Sidecar sidecar = PreviewPipeline.Run(Base with { Estimate = true, NoiseTarget = 0.5 }, new FakeRenderer());
        NoiseInfo noise = sidecar.Estimate!.Noise!;
        Assert.AreEqual(0.5, noise.TargetSteps);
        Assert.AreEqual(NoiseMeter.SamplesFor(noise.OneSampleSteps, 0.5), noise.SamplesForTarget);
        Assert.IsTrue(noise.SamplesForTargetLow <= noise.SamplesForTarget);
        Assert.IsTrue(noise.TotalForTarget!.Value.High >= noise.TraceForTarget!.Value.High);
        Assert.AreEqual(NoiseMeter.StepsAt(noise.OneSampleSteps, 16), noise.StepsAtSamples, 1e-12);
    }

    [TestMethod]
    public void Estimate_OfADenoisedFinal_TimesTheDenoiser()
    {
        Sidecar sidecar = PreviewPipeline.Run(Base with { Estimate = true, Denoise = new DenoiseSettings() }, new FakeRenderer(), denoiser: new FakeDenoiser());
        Assert.IsNotNull(sidecar.Estimate!.Denoise);
        Assert.AreEqual(sidecar.Estimate.Setup.Mid + sidecar.Estimate.Trace!.Value.Mid + sidecar.Estimate.Write!.Value.Mid + sidecar.Estimate.Denoise.Value.Mid,
            sidecar.Estimate.Total!.Value.Mid, 1e-9);
        Assert.IsFalse(File.Exists(P("p.denoised.png")));
    }

    [TestMethod]
    public void Estimate_OfAFinalThatDoesNotFit_ProbesNothing()
    {
        var renderer = new FakeRenderer { BudgetBytes = 1 };
        var notices = new List<string>();
        Sidecar sidecar = new PreviewSession(renderer) { Notice = notices.Add }.Run(Base with { Estimate = true });
        Assert.AreEqual(0, renderer.Renders.Count, "no image render");
        Assert.IsFalse(sidecar.Estimate!.Memory.Fits);
        Assert.IsNull(sidecar.Estimate.Total);
        Assert.IsNull(sidecar.Estimate.Probe);
        StringAssert.StartsWith(notices.Single(), "Does not fit");
        CollectionAssert.Contains(sidecar.Warnings.ToArray(), notices.Single());
    }

    [TestMethod]
    public void Estimate_RoundTrips()
    {
        PreviewPipeline.Run(Base with { Estimate = true, NoiseTarget = 1 }, new FakeRenderer());
        Sidecar back = VistaJson.Deserialize<Sidecar>(File.ReadAllText(P("p.estimate.json")));
        Assert.AreEqual(Sidecar.CurrentSchema, back.Schema);
        Assert.AreEqual(16, back.Estimate!.Probe!.SamplesPerPixel);
        Assert.IsNotNull(back.Estimate.Noise!.SamplesForTarget);
    }

    [TestMethod]
    public void Final_RecordsItsMemoryAndClock_AndReportsItsProgress()
    {
        var seen = new List<TraceProgress>();
        Sidecar sidecar = new PreviewSession(new FakeRenderer()) { Progress = seen.Add }.Run(Base);
        MemoryInfo memory = sidecar.Render!.Memory!;
        Assert.AreEqual(FakeRenderer.PlanBaseBytes, memory.BaseBytes);
        Assert.IsTrue(memory.Fits && !memory.Tight);
        Assert.IsTrue(memory.PlannedBytes.ContainsKey("imageXyz"));
        TraceInfo trace = sidecar.Render.Trace!;
        Assert.IsTrue(trace.Passes >= 1);
        Assert.AreEqual(TraceClock.CheckpointShares.Count, trace.Checkpoints.Count);
        Assert.IsTrue(trace.Checkpoints.All(c => c.ActualRemainingSeconds is not null));
        Assert.AreEqual(1.0, seen[^1].Progress);

        Sidecar back = VistaJson.Deserialize<Sidecar>(File.ReadAllText(P("p.json")));
        Assert.AreEqual(trace.Passes, back.Render!.Trace!.Passes);
        Assert.AreEqual(memory.PlannedPeakBytes, back.Render.Memory!.PlannedPeakBytes);
    }

    [TestMethod]
    public void Final_WithItsVariance_RecordsTheNoiseItShows()
    {
        Assert.IsNull(PreviewPipeline.Run(Base, new FakeRenderer()).Render!.NoiseSteps);
        Sidecar sidecar = PreviewPipeline.Run(Base with { Debug = new HashSet<DebugOutput> { DebugOutput.Variance } }, new FakeRenderer());
        Assert.IsTrue(sidecar.Render!.NoiseSteps > 0.0);
    }

    [TestMethod]
    public void Final_ThatDoesNotFit_IsRefusedBeforeItRenders()
    {
        var renderer = new FakeRenderer { BudgetBytes = 1 };
        InsufficientMemoryException e = Assert.ThrowsExactly<InsufficientMemoryException>(() => PreviewPipeline.Run(Base, renderer));
        StringAssert.StartsWith(e.Message, "Does not fit");
        Assert.AreEqual(0, renderer.Renders.Count);
    }

    [TestMethod]
    public void Final_ThatIsTight_IsWarnedAboutAtOnce()
    {
        var probe = new FakeRenderer();
        long peak = PreviewPipeline.Run(Base, probe).Render!.Memory!.PlannedPeakBytes;
        var notices = new List<string>();
        Sidecar sidecar = new PreviewSession(new FakeRenderer { BudgetBytes = (long)(peak / 0.9) }) { Notice = notices.Add }.Run(Base);
        StringAssert.StartsWith(notices.Single(), "Tight");
        CollectionAssert.Contains(sidecar.Warnings.ToArray(), notices.Single());
        Assert.IsTrue(sidecar.Render!.Memory!.Tight);
    }
}
