using Vista.Cli;
using Vista.Output;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// The estimator's math (vista-plan §7.2, §10 M1.15): the trace's clock and its live ETA, the projection from a probe's passes, the
/// ranges, the noise in steps of the PNG and the samples a target needs, the video memory's plan, and which stages a final repeats.
/// </summary>
[TestClass]
public sealed class RenderEstimateTests
{
    /// <summary>A clock in microseconds that the test moves.</summary>
    private sealed class MicroTime : TimeProvider
    {
        private long _now;

        public override long TimestampFrequency => 1_000_000;

        public override long GetTimestamp() => _now;

        public void Advance(double seconds) => _now += (long)Math.Round(seconds * 1e6);
    }

    // A 100-pixel-wide image of four rows whose top two rows cost 1 µs a pixel-sample and bottom two 3 µs, traced one row of one
    // sample at a time (the budget is too small for more), the wall taking twice the GPU time.
    private static (BatchPlanner Planner, TraceClock Clock, List<TraceProgress> Seen) Trace(int samples, Action<BatchPlanner, TraceClock>? afterBatch = null)
    {
        var time = new MicroTime();
        var planner = new BatchPlanner(100, 4, samples, new BatchPlannerOptions { DispatchBudgetMs = 0.1 });
        var clock = new TraceClock(planner, time);
        var seen = new List<TraceProgress>();
        clock.Progressed += seen.Add;
        while (planner.HasNext)
        {
            SampleBatch batch = planner.Next();
            Assert.AreEqual((1, 1), (batch.RowCount, batch.SampleCount), "one row of one sample a batch");
            var gpu = TimeSpan.FromTicks(batch.RowStart < 2 ? 1000 : 3000); // 100 pixels at 1 or 3 µs
            time.Advance(2.0 * gpu.TotalSeconds);
            planner.Complete(batch, gpu);
            afterBatch?.Invoke(planner, clock);
        }
        return (planner, clock, seen);
    }

    [TestMethod]
    public void Clock_SumsItsPasses()
    {
        (_, TraceClock clock, _) = Trace(3);
        Assert.AreEqual(3, clock.Passes.Count);
        foreach (PassTiming pass in clock.Passes)
        {
            Assert.AreEqual(1, pass.SampleCount);
            Assert.AreEqual(400L, pass.PixelSamples);
            Assert.AreEqual(4, pass.Dispatches);
            Assert.AreEqual(800e-6, pass.GpuSeconds, 1e-9, "100 × (1 + 1 + 3 + 3) µs");
            Assert.AreEqual(1600e-6, pass.WallSeconds, 1e-9);
            Assert.AreEqual(2000.0, pass.CostNs, 1e-6);
        }
        Assert.AreEqual(2400e-6, clock.GpuSeconds, 1e-9);
        Assert.AreEqual(4800e-6, clock.WallSeconds, 1e-9);
    }

    [TestMethod]
    public void Clock_PredictsNothingUntilEveryRowHasACost_ThenEachRowsOwn()
    {
        var predictions = new List<double?>();
        Trace(3, (_, clock) => predictions.Add(clock.RemainingSeconds()));
        for (int i = 0; i < 3; i++)
            Assert.IsNull(predictions[i], $"after batch {i + 1} of the first pass a row has no cost yet");
        // After the first pass: two samples of every row left, at each row's own cost, twice on the wall.
        Assert.AreEqual(2 * 2 * 800e-6, predictions[3]!.Value, 1e-9);
        // Halfway through the second pass (rows 0 and 1 done, the dear rows left): 1 sample of the cheap rows, 2 of the dear ones.
        Assert.AreEqual(2 * 100 * (1e-6 + 1e-6 + 2 * 3e-6 + 2 * 3e-6), predictions[5]!.Value, 1e-9);
        Assert.AreEqual(0.0, predictions[^1]!.Value);
    }

    [TestMethod]
    public void Clock_RecordsItsCheckpoints_AndWhatWasLeft()
    {
        (_, TraceClock clock, List<TraceProgress> seen) = Trace(3);
        CollectionAssert.AreEqual(TraceClock.CheckpointShares.ToArray(), clock.Checkpoints.Select(c => c.Progress).ToArray());
        Assert.IsNull(clock.Checkpoints[0].RemainingSeconds, "10% is reached during the first pass");
        Assert.IsNull(clock.Checkpoints[1].RemainingSeconds, "and so is 25%, one row short of a cost for every row");
        foreach (EtaCheckpoint c in clock.Checkpoints.Skip(2))
        {
            // Every row costs the same each pass, so the ETA is exact once it has a cost for every row.
            Assert.IsNotNull(c.ActualRemainingSeconds);
            Assert.AreEqual(c.ActualRemainingSeconds!.Value, c.RemainingSeconds!.Value, 1e-9, $"at {c.Progress:P0}");
        }
        Assert.AreEqual(12, seen.Count, "one report a batch");
        Assert.AreEqual(1.0, seen[^1].Progress);
        Assert.AreEqual(3, seen[^1].PassesDone);
    }

    [TestMethod]
    public void Projection_TakesTheProbe_AndTheRestAtTheLastPassesCost()
    {
        // A frame of 1e9 pixels: the ramp, a pass of one sample, then passes of two at a dearer cost a sample.
        PassTiming[] passes =
        [
            new(0, 1, 1_000_000_000, 1.0, 3.0, 7),
            new(1, 1, 1_000_000_000, 1.0, 2.0, 1),
            new(2, 2, 2_000_000_000, 2.4, 4.8, 1),
            new(4, 2, 2_000_000_000, 2.6, 5.2, 1),
        ];
        var p = TraceProjection.From(passes, 1_000_000_000, rays: 18_000_000_000);
        Assert.AreEqual((4, 6, 2), (p.ProbePasses, p.ProbeSamples, p.SteadySamples));
        Assert.AreEqual(7.0, p.ProbeGpuSeconds, 1e-12);
        Assert.AreEqual(15.0, p.ProbeWallSeconds, 1e-12);
        Assert.AreEqual(1.25, p.CostNs, 1e-12, "the passes of the last one's size: 5 s over 4e9 pixel-samples");
        Assert.AreEqual(2.0, p.WallPerGpu, 1e-12);
        Assert.AreEqual(0.1 / 2.5, p.CostSpread, 1e-12);
        Assert.AreEqual(TraceProjection.MinMargin, p.Margin, "a spread under half the least margin leaves it");
        Assert.AreEqual(3.0, p.RaysPerSample, 1e-12);
        Assert.AreEqual(18e9 / 7.0, p.RaysPerSecond, 1e-3);

        // Ten samples: the probe's six as measured, four more at 1.25 ns and twice on the wall.
        Assert.AreEqual(7.0 + 4 * 1.25, p.GpuSeconds(10), 1e-9);
        TimeRange wall = p.WallSeconds(10);
        Assert.AreEqual(15.0 + 4 * 1.25 * 2.0, wall.Mid, 1e-9);
        Assert.AreEqual(wall.Mid * (1 - p.Margin), wall.Low, 1e-9);
        Assert.AreEqual(wall.Mid * (1 + p.Margin), wall.High, 1e-9);
        // Fewer samples than the probe traced: its share of the probe.
        Assert.AreEqual(7.0 / 2, p.GpuSeconds(3), 1e-9);
        Assert.AreEqual(15.0 / 2, p.WallSeconds(3).Mid, 1e-9);
    }

    [TestMethod]
    public void Projection_LeavesOutTheRamp_WhenEveryPassIsOneSample()
    {
        PassTiming[] passes = [new(0, 1, 100, 1e-6, 4e-6, 7), new(1, 1, 100, 2e-6, 4e-6, 1), new(2, 1, 100, 2e-6, 4e-6, 1)];
        var p = TraceProjection.From(passes, 100, rays: 300);
        Assert.AreEqual(20.0, p.CostNs, 1e-9, "the ramp's pass is not the steady cost");
        Assert.AreEqual(2.0, p.WallPerGpu, 1e-9);
    }

    [TestMethod]
    public void Projection_WidensItsMargin_ForPassesThatDisagree()
    {
        var p = TraceProjection.From([new(0, 1, 100, 1e-7, 2e-7, 1), new(1, 1, 100, 1e-7, 2e-7, 1), new(2, 1, 100, 1.5e-7, 3e-7, 1)], 100, rays: 300);
        Assert.AreEqual(0.5 / 2.5, p.CostSpread, 1e-9);
        Assert.AreEqual(0.4, p.Margin, 1e-9, "twice the spread");
    }

    [TestMethod]
    public void Projection_OfOnePass_UsesIt()
    {
        var p = TraceProjection.From([new(0, 4, 400, 4e-6, 8e-6, 1)], 100, rays: 1200);
        Assert.AreEqual((1, 4, 4), (p.ProbePasses, p.ProbeSamples, p.SteadySamples));
        Assert.AreEqual(10.0, p.CostNs, 1e-9);
        Assert.AreEqual(0.0, p.CostSpread);
        Assert.ThrowsExactly<ArgumentException>(() => TraceProjection.From([], 100, 0));
    }

    [TestMethod]
    public void Ranges_AddEndByEnd_AndReadAsDurations()
    {
        TimeRange a = TimeRange.Around(10.0, 0.1), b = TimeRange.Around(100.0, 0.2);
        Assert.AreEqual(9.0, a.Low, 1e-9);
        Assert.AreEqual(10.0, a.Mid);
        Assert.AreEqual(11.0, a.High, 1e-9);
        TimeRange sum = a + b;
        Assert.AreEqual(89.0, sum.Low, 1e-9);
        Assert.AreEqual(110.0, sum.Mid, 1e-9);
        Assert.AreEqual(131.0, sum.High, 1e-9);
        Assert.IsTrue(sum.Contains(89.0) && sum.Contains(131.0) && !sum.Contains(131.5));
        Assert.AreEqual(0.0, TimeRange.Around(10.0, 2.0).Low, "never below nothing");

        Assert.AreEqual("4.5 s", TimeRange.Describe(4.5));
        Assert.AreEqual("45 s", TimeRange.Describe(45.2));
        Assert.AreEqual("1 m 5 s", TimeRange.Describe(65.0));
        Assert.AreEqual("1 h 3 m", TimeRange.Describe(3780.0));
        Assert.AreEqual("?", TimeRange.Describe(double.NaN));
        Assert.AreEqual("9.0 s – 11 s (about 10 s)", a.ToString());
    }

    [TestMethod]
    public void Noise_ScalesWithSigmaOverLight_AndFallsAsTheRootOfTheSamples()
    {
        // Three grey pixels at mid-grey after the exposure: σ of 10% and 20% of the light, and a highlight far past white.
        float[] xyz = [0.18f, 0.18f, 0.18f, 0.18f, 0.18f, 0.18f, 500f, 500f, 500f];
        float[] linear = DisplayTransform.XyzToLinearRec709(xyz);
        int n = 16;
        float[] varianceOfMean = [(float)(0.018 * 0.018 / n), (float)(0.036 * 0.036 / n), 1f];
        float[] steps = NoiseMeter.OneSampleSteps(linear, xyz, varianceOfMean, n, 0.0, DisplayCurve.Agx, DisplayLook.Base);
        Assert.IsTrue(steps[0] > 1.0, $"10% noise at mid-grey shows: {steps[0]} steps");
        Assert.AreEqual(2.0, steps[1] / steps[0], 1e-4, "twice the σ, twice the steps");
        Assert.AreEqual(0.0, steps[2], 1e-3, "a clipped highlight shows no noise");

        // The same per-sample σ measured over other sample counts reads the same.
        float[] again = NoiseMeter.OneSampleSteps(linear[..3], xyz[..3], [(float)(0.018 * 0.018 / 64)], 64, 0.0, DisplayCurve.Agx, DisplayLook.Base);
        Assert.AreEqual(steps[0], again[0], 1e-3);

        Assert.AreEqual(1.0, NoiseMeter.StepsAt(4.0, 16), 1e-12);
        Assert.AreEqual(16, NoiseMeter.SamplesFor(4.0, 1.0));
        Assert.AreEqual(17, NoiseMeter.SamplesFor(4.1, 1.0), "rounded up");
        Assert.AreEqual(1, NoiseMeter.SamplesFor(0.1, 1.0), "at least one sample");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => NoiseMeter.SamplesFor(1.0, 0.0));
    }

    [TestMethod]
    public void Noise_OfBlack_IsWhatOneSigmaOfGreyShows()
    {
        float[] xyz = [0f, 0f, 0f, 0f, 0f, 0f];
        float[] steps = NoiseMeter.OneSampleSteps(new float[6], xyz, [0f, 0.01f], 4, 0.0, DisplayCurve.Agx, DisplayLook.Base);
        Assert.AreEqual(0.0, steps[0], 1e-6, "no light, no noise");
        Assert.IsTrue(steps[1] > 0.0, "light in some samples of a pixel black on average");
    }

    [TestMethod]
    public void Percentile_IsTheNearestRank()
    {
        float[] values = [.. Enumerable.Range(1, 100).Select(i => (float)i).Reverse()];
        Assert.AreEqual(95.0, NoiseMeter.PercentileOf(values, 0.95));
        Assert.AreEqual(100.0, NoiseMeter.PercentileOf(values, 1.0));
        Assert.AreEqual(1.0, NoiseMeter.PercentileOf(values, 0.0));
        Assert.AreEqual(0.0, NoiseMeter.PercentileOf([], 0.5));
    }

    [TestMethod]
    public void MemoryPlan_PeaksInItsFullestStage()
    {
        const long MiB = 1L << 20;
        var plan = new MemoryPlan(100 * MiB, 1000 * MiB,
        [
            new MemoryItem("mesh", 200 * MiB, MemoryStage.Mesh),
            new MemoryItem("blas", 300 * MiB, MemoryStage.Acceleration),
            new MemoryItem("blasScratch", 150 * MiB, MemoryStage.Acceleration, MemoryStage.Acceleration),
            new MemoryItem("ordersScratch", 50 * MiB, MemoryStage.Air, MemoryStage.Air),
            new MemoryItem("imageXyz", 120 * MiB, MemoryStage.Trace),
        ]);
        Assert.AreEqual(300 * MiB, plan.HeldIn(MemoryStage.Mesh));
        Assert.AreEqual(750 * MiB, plan.HeldIn(MemoryStage.Acceleration));
        Assert.AreEqual(650 * MiB, plan.HeldIn(MemoryStage.Air));
        Assert.AreEqual(720 * MiB, plan.HeldIn(MemoryStage.Trace));
        Assert.AreEqual(MemoryStage.Acceleration, plan.PeakStage);
        Assert.AreEqual(750 * MiB, plan.PeakBytes);
        Assert.IsTrue(plan.Fits && !plan.Tight);
        Assert.IsNull(plan.Warning());

        MemoryPlan tight = plan with { BudgetBytes = 900 * MiB };
        Assert.IsTrue(tight.Fits && tight.Tight);
        StringAssert.StartsWith(tight.Warning(), "Tight");
        MemoryPlan over = plan with { BudgetBytes = 700 * MiB };
        Assert.IsFalse(over.Fits);
        StringAssert.StartsWith(over.Warning(), "Does not fit");
        StringAssert.Contains(over.Warning(), "750 MiB");
        Assert.IsTrue(MemoryPlan.None.Fits, "an unknown budget never refuses");

        MemoryInfo info = MemoryInfo.Of(plan, measuredPeakBytes: 740 * MiB);
        Assert.AreEqual(150 * MiB, info.PlannedBytes["blasScratch"]);
        Assert.AreEqual(10.0 / 740.0, info.PlanError!.Value, 1e-12);
        Assert.IsNull(MemoryInfo.Of(plan, 0).PlanError);
    }

    [TestMethod]
    public void Page_RoundsUpTo64KiB()
    {
        Assert.AreEqual(0L, MemoryPlan.Page(0));
        Assert.AreEqual(65536L, MemoryPlan.Page(1));
        Assert.AreEqual(65536L, MemoryPlan.Page(65536));
        Assert.AreEqual(131072L, MemoryPlan.Page(65537));
    }

    [TestMethod]
    public void Setup_IsWhatTheFinalRepeats()
    {
        var timings = new Dictionary<string, double> { ["worldGen"] = 3.0, ["mesh"] = 12.0, ["render"] = 20.0, ["exposureMeter"] = 0.01 };
        var stages = new Dictionary<string, double>
        {
            ["shaderCompile"] = 1.5, ["atmosphereLuts"] = 8.8, ["scatteringOrdersGpu"] = 3.5, ["trace"] = 1.4, ["readback"] = 0.1,
        };
        SortedDictionary<string, double> setup = FinalEstimate.SetupStages(timings, stages);
        CollectionAssert.AreEqual(
            new[] { "exposureMeter", "mesh", "render.atmosphereLuts", "render.readback", "render.shaderCompile", "worldGen" }, setup.Keys.ToArray());
        TimeRange total = FinalEstimate.Total(TimeRange.Around(10, 0), TimeRange.Around(20, 0), TimeRange.Around(3, 0), null);
        Assert.AreEqual(33.0, total.Mid, 1e-12);
    }

    [TestMethod]
    public void Crop_TakesTheWindowOfEveryOutput()
    {
        // 4×3 pixels; each value its pixel's index (times ten plus the channel).
        float[] Image(int channels) => [.. Enumerable.Range(0, 12 * channels).Select(i => (float)(i / channels * 10 + i % channels))];
        var output = new RenderOutput(4, 3, Image(3), new Dictionary<Aov, float[]> { [Aov.Normal] = Image(3), [Aov.Depth] = Image(1) }, new RenderStats());
        RenderOutput crop = PreviewSession.Crop(output, new PixelWindow(1, 1, 2, 2));
        Assert.AreEqual((2, 2), (crop.Width, crop.Height));
        CollectionAssert.AreEqual(new float[] { 50, 51, 52, 60, 61, 62, 90, 91, 92, 100, 101, 102 }, crop.Xyz);
        CollectionAssert.AreEqual(crop.Xyz, crop.Aovs[Aov.Normal]);
        CollectionAssert.AreEqual(new float[] { 50, 60, 90, 100 }, crop.Aovs[Aov.Depth]);
        Assert.ThrowsExactly<ArgumentException>(() => PreviewSession.Crop(output, new PixelWindow(3, 0, 2, 1)));
    }
}
