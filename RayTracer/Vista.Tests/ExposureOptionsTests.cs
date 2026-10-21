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
    public void TheCommandLine_SetsTheLookAndTheCompensation()
    {
        Assert.AreEqual(DisplayTransform.DefaultLook, Parse().Look);
        Assert.IsNull(Parse().MeterCompensationEv, "the meter's own default");
        Assert.AreEqual(DisplayLook.Punchy, Parse("--look", "Punchy").Look);
        Assert.AreEqual(DisplayLook.Base, Parse("--look", "base").Look);
        Assert.AreEqual(0.5, Parse("--compensation", "0.5").MeterCompensationEv);
        Assert.AreEqual(-1.25, Parse("--compensation", "-1.25").MeterCompensationEv);
        Assert.AreEqual(0.0, Parse("--compensation", "0").MeterCompensationEv);
        StringAssert.Contains(Error("--look", "golden"), "--look must be base or punchy");
        StringAssert.Contains(Error("--look", "none"), "--look must be base or punchy");
        StringAssert.Contains(Error("--look", "2"), "--look must be base or punchy");
        StringAssert.Contains(Error("--compensation", "11"), "--compensation must be a number of stops within");
        StringAssert.Contains(Error("--compensation", "NaN"), "--compensation must be a number of stops within");
        StringAssert.Contains(Error("--compensation", "+1EV"), "--compensation must be a number of stops within");
    }

    [TestMethod]
    public void ASheet_VariesTheLookAndTheCompensation()
    {
        var request = new PreviewRequest { RecipePath = "r.json", Seed = 1, OutPath = "p.png" };
        Assert.IsTrue(SheetAxis.TryParse("look=base,punchy", out SheetAxis? look, out string? problem), problem);
        Assert.AreEqual(DisplayLook.Punchy, look!.Apply(request, 1).Look);
        Assert.IsTrue(SheetAxis.TryParse("compensation=0,0.5,1", out SheetAxis? compensation, out problem), problem);
        Assert.AreEqual(0.5, compensation!.Apply(request, 1).MeterCompensationEv);
        Assert.AreEqual("compensation=1", compensation.Label(2));
        Assert.IsFalse(SheetAxis.TryParse("look=base,golden", out _, out problem));
        StringAssert.Contains(problem, "not base or punchy");
        Assert.IsFalse(SheetAxis.TryParse("compensation=0,twelve", out _, out problem));
        StringAssert.Contains(problem, "not a number of stops");
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
        request = WatchCommandLine.Merge(watch, "--look punchy\n--compensation 0.5\n", problems, out _, out _);
        Assert.AreEqual(0, problems.Count, string.Join(" | ", problems));
        Assert.AreEqual((DisplayLook.Punchy, (double?)0.5), (request!.Look, request.MeterCompensationEv));
    }
}
