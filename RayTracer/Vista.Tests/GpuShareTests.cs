using Vista.Rendering;

namespace Vista.Tests;

/// <summary>The GPU's share (2026-10-26: the dev box is the user's own PC): a run takes about half of it by default, in short submissions.</summary>
[TestClass]
public sealed class GpuShareTests
{
    [TestMethod]
    public void TheShare_IsCappedByDefault_AndKeepsSubmissionsShort()
    {
        if (Environment.GetEnvironmentVariable(GpuShare.Variable) is { Length: > 0 })
            Assert.Inconclusive($"{GpuShare.Variable} is set in this environment.");
        Assert.AreEqual(GpuShare.DefaultShare, GpuShare.Share);
        Assert.IsTrue(GpuShare.Capped);
        Assert.AreEqual(GpuShare.CappedDispatchMs, GpuShare.Budget(250.0), "a long budget is held to the cap");
        Assert.AreEqual(12.5, GpuShare.Budget(12.5), "a shorter one stands");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        GpuShare.Pause(0.02);
        Assert.IsTrue(stopwatch.Elapsed.TotalSeconds >= 0.015, "half the GPU: as long a pause as the work");
        stopwatch.Restart();
        GpuShare.Pause(0.0);
        Assert.IsTrue(stopwatch.Elapsed.TotalSeconds < 0.01, "no work, no pause");
    }
}
