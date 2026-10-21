using Vista.Cli;

namespace Vista.Tests;

/// <summary>The contact-sheet CLI and its <c>--vary</c> axis (vista-plan §7.4).</summary>
[TestClass]
public sealed class SheetCommandLineTests
{
    private static SheetRequest Sheet(params string[] args)
    {
        Assert.IsTrue(SheetCommandLine.TryParse(args, out SheetRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
        return request!;
    }

    private static string SheetErrors(params string[] args)
    {
        Assert.IsFalse(SheetCommandLine.TryParse(args, out SheetRequest? request, out IReadOnlyList<string> errors));
        Assert.IsNull(request);
        return string.Join("\n", errors);
    }

    private static SheetFromRequest From(params string[] args)
    {
        Assert.IsTrue(SheetCommandLine.TryParseFrom(args, out SheetFromRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
        return request!;
    }

    private static string FromErrors(params string[] args)
    {
        Assert.IsFalse(SheetCommandLine.TryParseFrom(args, out SheetFromRequest? request, out IReadOnlyList<string> errors));
        Assert.IsNull(request);
        return string.Join("\n", errors);
    }

    [TestMethod]
    public void Sheet_TakesEveryPreviewOption()
    {
        SheetRequest r = Sheet("--sheet", "--recipe", "r.json", "--vary", "seed=1,2,3,4", "--out", "s.png", "--width", "640",
            "--spp", "16", "--loupe", "10,10,64,64", "--debug", "normal", "--columns", "4", "--title", "four seeds");
        Assert.AreEqual("seed", r.Axis.Key);
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4" }, r.Axis.Values.ToArray());
        Assert.AreEqual((640, 360, 16), (r.Base.Width, r.Base.Height, r.Base.SamplesPerPixel));
        Assert.AreEqual(new Rendering.PixelWindow(10, 10, 64, 64), r.Base.Loupe);
        Assert.AreEqual("s.png", r.Base.OutPath);
        Assert.AreEqual((4, "four seeds"), (r.Columns, r.Title));
        Assert.AreEqual(0, Sheet("--sheet", "--recipe", "r", "--vary", "seed=1", "--out", "s.png").Columns, "0 picks the default");
    }

    [TestMethod]
    public void Sheet_AxisReplacesTheOptionItSupplies()
    {
        Assert.AreEqual(string.Empty, Sheet("--sheet", "--seed", "1", "--vary", "recipe=a.json,b.json", "--out", "s.png").Base.RecipePath);
        StringAssert.Contains(SheetErrors("--sheet", "--recipe", "r", "--seed", "1", "--vary", "seed=1,2", "--out", "s.png"), "both choose the seed");
        StringAssert.Contains(SheetErrors("--sheet", "--recipe", "r", "--seed", "1", "--vary", "recipe=a,b", "--out", "s.png"), "both choose the recipe");
        StringAssert.Contains(SheetErrors("--sheet", "--recipe", "r", "--seed", "1", "--camera", "c", "--vary", "camera=a,b", "--out", "s.png"),
            "both choose the camera");
        StringAssert.Contains(SheetErrors("--sheet", "--recipe", "r", "--vary", "time=dawn", "--out", "s.png"), "--seed is required");
    }

    [TestMethod]
    [DataRow("--location", "zermatt", "latitude=0,45")]
    [DataRow("--latitude", "10", "location=zermatt,banff")]
    [DataRow("--time", "17:30", "time=dawn,dusk")]
    [DataRow("--time", "dawn", "solarTime=06:00,07:00")]
    [DataRow("--date", "2026-10-15", "season=spring,autumn")]
    public void Sheet_PlaceOptionAndAxisOverTheSameField_Conflict(string option, string value, string vary) =>
        StringAssert.Contains(SheetErrors("--sheet", "--recipe", "r.json", "--seed", "1", "--out", "s.png", option, value, "--vary", vary), "give one");

    [TestMethod]
    public void Sheet_PlaceOptionsCarryIntoEveryCell()
    {
        SheetRequest r = Sheet("--sheet", "--recipe", "r.json", "--seed", "1", "--out", "s.png", "--location", "zermatt",
            "--date", "2026-10-15", "--vary", "time=dawn,day,goldenHour,dusk");
        PreviewRequest dusk = r.Axis.Apply(r.Base, 3);
        Assert.AreEqual("\"zermatt\"", dusk.SliderOverrides["location"]);
        Assert.AreEqual("\"2026-10-15\"", dusk.SliderOverrides["date"]);
        Assert.AreEqual("\"dusk\"", dusk.SliderOverrides["time"]);
    }

    [TestMethod]
    public void Sheet_ProblemsAreReportedTogether()
    {
        string all = SheetErrors("--sheet", "--recipe", "r", "--seed", "1", "--out", "s.jpg", "--columns", "0", "--bogus", "x");
        foreach (string expected in new[] { "--vary is required", "--out must name a .png", "--columns must be", "'--bogus'" })
            StringAssert.Contains(all, expected);
        StringAssert.Contains(SheetErrors("--sheet", "--recipe", "r", "--seed", "1", "--vary", "seed=1,2", "--columns", "3", "--out", "s.png"),
            "at most the number of cells");
        StringAssert.Contains(SheetErrors("--preview"), "--sheet");
    }

    [TestMethod]
    [DataRow("seed", "--vary must look like")]
    [DataRow("seed=", "--vary must look like")]
    [DataRow("=1,2", "--vary must look like")]
    [DataRow("seed=1,x", "--vary seed")]
    [DataRow("seed=-1", "--vary seed")]
    [DataRow("spp=0,4", "outside")]
    [DataRow("bounces=65", "outside")]
    [DataRow("time=dawn,,dusk", "empty item")]
    [DataRow("setPieces=[lake", "Unbalanced")]
    [DataRow("camera.=1", "malformed field path")]
    [DataRow("a..b=1", "malformed field path")]
    public void Axis_RejectsMalformedValues(string vary, string message) =>
        StringAssert.Contains(SheetErrors("--sheet", "--recipe", "r", "--seed", "1", "--out", "s.png", "--vary", vary), message);

    [TestMethod]
    public void Axis_TooManyValues_IsRejected() =>
        StringAssert.Contains(
            SheetErrors("--sheet", "--recipe", "r", "--out", "s.png", "--vary", "seed=" + string.Join(",", Enumerable.Range(1, SheetAxis.MaxValues + 1))),
            "at most");

    [TestMethod]
    public void Axis_SetsTheFieldItVaries()
    {
        var baseRequest = new PreviewRequest { RecipePath = "r.json", Seed = 1, OutPath = "p.png", CameraPath = "c.json" };
        PreviewRequest Apply(string vary, int index)
        {
            Assert.IsTrue(SheetAxis.TryParse(vary, out SheetAxis? axis, out string? error), error);
            return axis!.Apply(baseRequest, index);
        }

        Assert.AreEqual(18446744073709551615ul, Apply("seed=3,18446744073709551615", 1).Seed);
        Assert.AreEqual(256, Apply("spp=16,256", 1).SamplesPerPixel);
        Assert.AreEqual(0, Apply("bounces=0,3", 0).MaxBounces);
        Assert.AreEqual("b.json", Apply("recipe=a.json,b.json", 1).RecipePath);
        Assert.AreEqual("a.json", Apply("camera=a.json,default", 0).CameraPath);
        Assert.IsNull(Apply("camera=a.json,default", 1).CameraPath, "default is the default camera");
        Assert.AreEqual("\"goldenHour\"", Apply("time=dawn,goldenHour", 1).SliderOverrides["time"]);
        Assert.AreEqual("[\"snowCappedMountains\",\"lake\"]",
            Apply("setPieces=[snowCappedMountains],[snowCappedMountains, lake]", 1).RecipeOverrides["setPieces"]);
        PreviewRequest yawed = Apply("camera.yawDegrees=0,90", 1);
        Assert.AreEqual("90", yawed.CameraOverrides["yawDegrees"]);
        Assert.AreEqual(0, yawed.RecipeOverrides.Count);
        Assert.AreEqual("c.json", yawed.CameraPath, "a camera field is set on the given camera");
        Assert.AreEqual(0, baseRequest.CameraOverrides.Count, "the base request is not changed");
    }

    [TestMethod]
    public void Axis_LabelsAreKeyEqualsValue()
    {
        Assert.IsTrue(SheetAxis.TryParse("time=dawn, dusk", out SheetAxis? axis, out _));
        Assert.AreEqual("time=dusk", axis!.Label(1));
        Assert.IsTrue(axis.SuppliesSeed is false && axis.SuppliesRecipe is false && axis.SuppliesCamera is false);
    }

    [TestMethod]
    public void SheetFrom_TakesImagesThenOptions()
    {
        SheetFromRequest r = From("--sheet-from", "main.png", "branch.png", "--out", "ba.png", "--labels", "before, after", "--columns", "2", "--title", "M1.1");
        CollectionAssert.AreEqual(new[] { "main.png", "branch.png" }, r.Inputs.ToArray());
        CollectionAssert.AreEqual(new[] { "before", "after" }, r.Labels.ToArray());
        Assert.AreEqual(("ba.png", 2, "M1.1"), (r.OutPath, r.Columns, r.Title));
        Assert.AreEqual(0, From("--sheet-from", "a.png", "--out", "s.png").Labels.Count, "labels come from the sidecars");
    }

    [TestMethod]
    public void SheetFrom_ChecksItsInputs()
    {
        StringAssert.Contains(FromErrors("--sheet-from", "--out", "s.png"), "at least one image");
        StringAssert.Contains(FromErrors("--sheet-from", "a.exr", "--out", "s.png"), "'a.exr' is not a .png");
        StringAssert.Contains(FromErrors("--sheet-from", "a.png", "b.png"), "--out is required");
        StringAssert.Contains(FromErrors("--sheet-from", "a.png", "b.png", "--out", "s.png", "--labels", "one"), "1 labels for 2 images");
        StringAssert.Contains(FromErrors("--sheet-from", "a.png", "--out", "s.png", "--columns", "2"), "at most the number of images");
        StringAssert.Contains(FromErrors("--sheet-from", "a.png", "--out", "s.png", "--dry-run"), "--dry-run");
        StringAssert.Contains(FromErrors("--sheet-from", "a.png", "--out", "s.png", "--spp", "4"), "'--spp'");
    }
}
