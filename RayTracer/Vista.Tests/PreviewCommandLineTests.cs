using Vista.Cli;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>The headless preview CLI arguments (vista-plan §7.4).</summary>
[TestClass]
public sealed class PreviewCommandLineTests
{
    private static PreviewRequest Parse(params string[] args)
    {
        Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
        return request!;
    }

    private static IReadOnlyList<string> Errors(params string[] args)
    {
        Assert.IsFalse(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors));
        Assert.IsNull(request);
        return errors;
    }

    [TestMethod]
    public void Minimal_UsesThe320By180Preview()
    {
        PreviewRequest r = Parse("--preview", "--recipe", "r.json", "--seed", "42", "--out", "p.png");
        Assert.AreEqual("r.json", r.RecipePath);
        Assert.AreEqual(42ul, r.Seed);
        Assert.AreEqual("p.png", r.OutPath);
        Assert.IsNull(r.CameraPath);
        Assert.AreEqual((320, 180), (r.Width, r.Height));
        Assert.AreEqual(64, r.SamplesPerPixel);
        Assert.AreEqual(6, r.MaxBounces);
        Assert.AreEqual(0, r.Debug.Count);
        Assert.AreEqual(new BatchPlannerOptions().DispatchBudgetMs, r.DispatchBudgetMs);
        Assert.IsFalse(r.DryRun);
    }

    [TestMethod]
    public void Everything_IsParsed()
    {
        PreviewRequest r = Parse("--preview", "--dry-run", "--recipe", "r.json", "--seed", "18446744073709551615", "--camera", "c.json",
            "--out", "out/p.PNG", "--width", "640", "--aspect", "4:3", "--spp", "16", "--bounces", "3", "--debug", "depth, tier",
            "--dispatch-ms", "12.5");
        Assert.AreEqual(ulong.MaxValue, r.Seed);
        Assert.AreEqual("c.json", r.CameraPath);
        Assert.AreEqual((640, 480), (r.Width, r.Height));
        Assert.AreEqual(16, r.SamplesPerPixel);
        Assert.AreEqual(3, r.MaxBounces);
        CollectionAssert.AreEquivalent(new[] { DebugOutput.Depth, DebugOutput.Tier }, r.Debug.ToArray());
        Assert.AreEqual(12.5, r.DispatchBudgetMs);
        Assert.IsTrue(r.DryRun);
    }

    [TestMethod]
    [DataRow("21:9", 137)]
    [DataRow("9:19.5", -1)]
    [DataRow("9:16", 569)]
    public void Aspect_SetsTheHeight(string aspect, int height)
    {
        string[] args = ["--preview", "--recipe", "r", "--seed", "1", "--out", "p.png", "--aspect", aspect];
        if (height < 0)
            StringAssert.Contains(Errors(args)[0], "--aspect");
        else
            Assert.AreEqual(height, Parse(args).Height);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("1000.5")]
    [DataRow("-5")]
    [DataRow("fast")]
    public void DispatchBudget_MustFitUnderTheTdr(string value) =>
        StringAssert.Contains(Errors("--preview", "--recipe", "r", "--seed", "1", "--out", "p.png", "--dispatch-ms", value)[0], "--dispatch-ms");

    [TestMethod]
    public void DebugAll_SelectsEveryOutput() =>
        Assert.AreEqual(Enum.GetValues<DebugOutput>().Length, Parse("--preview", "--recipe", "r", "--seed", "1", "--out", "p.png", "--debug", "all").Debug.Count);

    [TestMethod]
    public void AllProblems_AreReportedTogether()
    {
        IReadOnlyList<string> errors = Errors("--preview", "--seed", "-3", "--out", "p.jpg", "--width", "0", "--debug", "albedo,sparkle,1", "--bogus");
        string all = string.Join("\n", errors);
        foreach (string expected in new[] { "--recipe is required", "--seed must be", "--out must name a .png", "--width must be", "'sparkle'", "'1'", "'--bogus'" })
            StringAssert.Contains(all, expected);
    }

    [TestMethod]
    public void ModeAndValues_AreChecked()
    {
        StringAssert.Contains(Errors("--recipe", "r")[0], "--preview");
        StringAssert.Contains(string.Join("\n", Errors("--preview", "--recipe", "--seed", "1", "--out", "p.png")), "--recipe needs a value");
        StringAssert.Contains(string.Join("\n", Errors("--preview", "--recipe", "a", "--recipe", "b", "--seed", "1", "--out", "p.png")), "more than once");
    }
}
