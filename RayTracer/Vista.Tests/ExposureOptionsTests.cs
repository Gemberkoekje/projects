using Vista.Cli;
using Vista.Output;

namespace Vista.Tests;

/// <summary>How a request asks for its exposure and display curve (vista-plan §10 M1.10a): the CLI options, the sheet axes and the watch's options file.</summary>
[TestClass]
public sealed class ExposureOptionsTests
{
    private static PreviewRequest Parse(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
        return request!;
    }

    private static string Error(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsFalse(PreviewCommandLine.TryParse(args, out _, out IReadOnlyList<string> errors));
        return string.Join(" | ", errors);
    }

    [TestMethod]
    public void TheCommandLine_SetsTheMeterAndTheCurve()
    {
        Assert.AreEqual((Metering.Incident, DisplayCurve.Agx), (Parse().Meter, Parse().Display), "the incident meter and AgX by default");
        PreviewRequest old = Parse("--meter", "average", "--display", "aces");
        Assert.AreEqual((Metering.Average, DisplayCurve.Aces), (old.Meter, old.Display));
        Assert.AreEqual((Metering.Incident, DisplayCurve.Agx), (Parse("--meter", "Incident", "--display", "AGX").Meter, Parse("--display", "agx").Display));
        StringAssert.Contains(Error("--meter", "spot"), "--meter must be incident or average");
        StringAssert.Contains(Error("--meter", "none"), "--meter must be incident or average");
        StringAssert.Contains(Error("--meter", "1"), "--meter must be incident or average");
        StringAssert.Contains(Error("--display", "filmic"), "--display must be agx or aces");
        StringAssert.Contains(Error("--display", "none"), "--display must be agx or aces");
    }

    [TestMethod]
    public void ASheet_VariesTheMeterAndTheCurve()
    {
        var request = new PreviewRequest { RecipePath = "r.json", Seed = 1, OutPath = "p.png" };
        Assert.IsTrue(SheetAxis.TryParse("meter=average,incident", out SheetAxis? meter, out string? problem), problem);
        Assert.AreEqual(Metering.Average, meter!.Apply(request, 0).Meter);
        Assert.AreEqual("meter=incident", meter.Label(1));
        Assert.IsTrue(SheetAxis.TryParse("display=aces,agx", out SheetAxis? display, out problem), problem);
        Assert.AreEqual(DisplayCurve.Aces, display!.Apply(request, 0).Display);
        Assert.IsFalse(SheetAxis.TryParse("meter=average,spot", out _, out problem));
        StringAssert.Contains(problem, "not incident or average");
        Assert.IsFalse(SheetAxis.TryParse("display=agx,reinhard", out _, out problem));
        StringAssert.Contains(problem, "not agx or aces");
    }

    [TestMethod]
    public void AWatchsOptionsFile_ChangesThem()
    {
        var watch = new WatchRequest
        {
            Values = new Dictionary<string, string> { ["--recipe"] = "r.json", ["--seed"] = "1", ["--out"] = "p.png" },
            Flags = new HashSet<string>(),
        };
        var problems = new List<string>();
        PreviewRequest? request = WatchCommandLine.Merge(watch, "--meter average  # the old meter\n--display aces\n", problems, out _, out _);
        Assert.AreEqual(0, problems.Count, string.Join(" | ", problems));
        Assert.AreEqual((Metering.Average, DisplayCurve.Aces), (request!.Meter, request.Display));
    }
}
