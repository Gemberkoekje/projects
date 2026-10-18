using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// TDR-safe batching (vista-plan §5.1 / §7.3): every (pixel, sample) is issued exactly once, dispatches stay
/// inside the budget once the cost is measured, and a slowdown shrinks the very next dispatch.
/// </summary>
[TestClass]
public sealed class BatchPlannerTests
{
    // Simulates a GPU whose true cost per pixel-sample is costNs(batch); returns every batch issued.
    private static List<SampleBatch> Drive(BatchPlanner planner, Func<SampleBatch, double> costNs)
    {
        var batches = new List<SampleBatch>();
        while (planner.HasNext)
        {
            SampleBatch batch = planner.Next();
            batches.Add(batch);
            double ns = batch.PixelSamples(planner.Width) * costNs(batch);
            planner.Complete(batch, TimeSpan.FromTicks(Math.Max(1L, (long)(ns / 100.0))));
        }
        Assert.IsTrue(planner.IsComplete);
        return batches;
    }

    private static void AssertExactCover(BatchPlanner planner, IEnumerable<SampleBatch> batches)
    {
        var count = new int[planner.Height, planner.SamplesPerPixel];
        foreach (SampleBatch b in batches)
        {
            for (int row = b.RowStart; row < b.RowStart + b.RowCount; row++)
            {
                for (int s = b.FirstSample; s < b.FirstSample + b.SampleCount; s++)
                    count[row, s]++;
            }
        }
        foreach (int c in count)
            Assert.AreEqual(1, c);
        Assert.AreEqual(planner.PixelSamplesTotal, planner.PixelSamplesCompleted);
        Assert.AreEqual(1.0, planner.Progress);
    }

    [TestMethod]
    [DataRow(320, 180, 64, 50.0)]
    [DataRow(37, 23, 10, 5000.0)]
    [DataRow(3840, 2160, 16, 400.0)]
    [DataRow(1, 1, 3, 1.0)]
    public void EveryPixelSampleIsIssuedExactlyOnce(int width, int height, int spp, double trueCostNs)
    {
        var planner = new BatchPlanner(width, height, spp);
        AssertExactCover(planner, Drive(planner, _ => trueCostNs));
    }

    [TestMethod]
    public void VaryingCost_StillCoversExactlyOnce()
    {
        var planner = new BatchPlanner(640, 360, 24, new BatchPlannerOptions { DispatchBudgetMs = 20 });
        AssertExactCover(planner, Drive(planner, b => b.Index % 3 == 0 ? 3000.0 : 200.0));
    }

    [TestMethod]
    public void FirstDispatch_IsBoundedByThePessimisticCost()
    {
        var options = new BatchPlannerOptions();
        var planner = new BatchPlanner(3840, 2160, 256, options);
        SampleBatch first = planner.Next();
        double predictedMs = first.PixelSamples(3840) * options.InitialCostNs / 1e6;
        Assert.IsTrue(predictedMs <= options.DispatchBudgetMs, $"{predictedMs} ms");
        Assert.AreEqual(1, first.SampleCount);
        Assert.IsTrue(first.RowCount < 2160, "a 4K frame at the pessimistic cost must be split into bands");
    }

    [TestMethod]
    public void MeasuredDispatches_StayWithinBudget()
    {
        const double trueCostNs = 120.0;
        var options = new BatchPlannerOptions { DispatchBudgetMs = 100 };
        var planner = new BatchPlanner(3840, 2160, 32, options);
        List<SampleBatch> batches = Drive(planner, _ => trueCostNs);
        foreach (SampleBatch b in batches.Skip(1))
        {
            double ms = b.PixelSamples(3840) * trueCostNs / 1e6;
            Assert.IsTrue(ms <= options.DispatchBudgetMs * 1.0001, $"batch {b.Index}: {ms} ms");
        }
        Assert.AreEqual(trueCostNs, planner.CostNsPerPixelSample, 1.0);
    }

    [TestMethod]
    public void SmallFrames_TakeSeveralSamplesPerFullFrameDispatch()
    {
        var planner = new BatchPlanner(320, 180, 64);
        planner.Complete(planner.Next(), TimeSpan.FromMilliseconds(1)); // measure: cheap
        SampleBatch next = planner.Next();
        Assert.AreEqual(0, next.RowStart);
        Assert.AreEqual(180, next.RowCount);
        Assert.IsTrue(next.SampleCount > 1);
        Assert.IsTrue(next.SampleCount <= new BatchPlannerOptions().MaxSamplesPerDispatch);
    }

    [TestMethod]
    public void Slowdown_ShrinksTheNextDispatchImmediately()
    {
        var planner = new BatchPlanner(3840, 2160, 8, new BatchPlannerOptions { DispatchBudgetMs = 100, InitialCostNs = 100 });
        SampleBatch a = planner.Next();
        planner.Complete(a, TimeSpan.FromTicks((long)(a.PixelSamples(3840) * 100.0 / 100.0)));  // as predicted
        SampleBatch b = planner.Next();
        planner.Complete(b, TimeSpan.FromTicks((long)(b.PixelSamples(3840) * 1000.0 / 100.0))); // ten times slower
        Assert.AreEqual(1000.0, planner.CostNsPerPixelSample, 1.0);
        SampleBatch c = planner.Next();
        Assert.IsTrue(c.PixelSamples(3840) * 1000.0 / 1e6 <= 100.0 * 1.0001);
    }

    [TestMethod]
    public void Speedup_RelaxesTheEstimateGradually()
    {
        var planner = new BatchPlanner(100, 100, 4, new BatchPlannerOptions { InitialCostNs = 1000 });
        SampleBatch a = planner.Next();
        planner.Complete(a, TimeSpan.FromTicks((long)(a.PixelSamples(100) * 200.0 / 100.0)));
        Assert.AreEqual(600.0, planner.CostNsPerPixelSample, 1.0); // halfway from 1000 toward 200
    }

    [TestMethod]
    public void Eta_TracksRemainingWork()
    {
        var planner = new BatchPlanner(100, 100, 10, new BatchPlannerOptions { InitialCostNs = 1000 });
        Assert.AreEqual(100.0, planner.EstimatedRemaining.TotalMilliseconds, 1e-6); // 1e5 pixel-samples × 1 µs
    }

    [TestMethod]
    public void Misuse_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new BatchPlanner(10, 10, 1, new BatchPlannerOptions { DispatchBudgetMs = 1500 }));
        Assert.ThrowsExactly<ArgumentException>(() => new BatchPlanner(10, 10, 1, new BatchPlannerOptions { InitialCostNs = double.NaN }));
        var planner = new BatchPlanner(10, 10, 1);
        Assert.ThrowsExactly<ArgumentException>(() => planner.Complete(new SampleBatch(0, 0, 10, 0, 1), TimeSpan.FromMilliseconds(1)));
        SampleBatch only = planner.Next();
        Assert.IsFalse(planner.HasNext);
        Assert.ThrowsExactly<InvalidOperationException>(() => planner.Next());
        planner.Complete(only, TimeSpan.FromMilliseconds(1));
        Assert.ThrowsExactly<ArgumentException>(() => planner.Complete(only, TimeSpan.FromMilliseconds(1)));
    }

    [TestMethod]
    public void OutOfOrderCompletion_IsFine_DoubleOrForgedCompletionIsNot()
    {
        // A host may keep several dispatches in flight; each must be reported exactly once, as issued.
        var planner = new BatchPlanner(64, 64, 8, new BatchPlannerOptions { InitialCostNs = 1e5 });
        SampleBatch a = planner.Next();
        SampleBatch b = planner.Next();
        planner.Complete(b, TimeSpan.FromMilliseconds(1));
        Assert.ThrowsExactly<ArgumentException>(() => planner.Complete(b, TimeSpan.FromMilliseconds(1)));
        Assert.ThrowsExactly<ArgumentException>(() => planner.Complete(a with { RowCount = a.RowCount + 1 }, TimeSpan.FromMilliseconds(1)));
        planner.Complete(a, TimeSpan.FromMilliseconds(1));
        Assert.IsFalse(planner.IsComplete, "more work remains");
    }
}
