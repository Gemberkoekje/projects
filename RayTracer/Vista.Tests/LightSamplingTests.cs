using System.Text.Json.Nodes;
using Vista.Cli;
using Vista.Output;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// M1.5's plumbing outside the shader (vista-plan §10 M1.5): the render settings' defaults, the <c>--light-sampling</c> and
/// <c>--rr-start</c> options, the sheet axis, the schema-6 sidecar and the sheet labels.
/// </summary>
[TestClass]
public sealed class LightSamplingTests
{
    private string _dir = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    private static PreviewRequest Parse(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" ", errors));
        return request!;
    }

    private static IReadOnlyList<string> Errors(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsFalse(PreviewCommandLine.TryParse(args, out _, out IReadOnlyList<string> errors));
        return errors;
    }

    [TestMethod]
    public void Settings_SampleSunAndSky_WithRouletteFromTheSecondBounce_OnStreamZero()
    {
        var settings = new RenderSettings { Width = 4, Height = 4 };
        Assert.AreEqual(LightSampling.Mis, settings.LightSampling);
        Assert.AreEqual(2, settings.RussianRouletteStart);
        Assert.AreEqual(RenderSettings.DefaultRussianRouletteStart, settings.RussianRouletteStart);
        Assert.AreEqual(0u, settings.RandomStream, "every image anyone sees draws stream 0");
    }

    [TestMethod]
    public void LightSamplingOption_PicksTheEstimator()
    {
        Assert.AreEqual(LightSampling.Mis, Parse().LightSampling);
        Assert.AreEqual(LightSampling.SunOnly, Parse("--light-sampling", "sunOnly").LightSampling);
        Assert.AreEqual(LightSampling.Mis, Parse("--light-sampling", "MIS").LightSampling);
        StringAssert.Contains(Errors("--light-sampling", "none")[0], "mis or sunOnly");
        StringAssert.Contains(Errors("--light-sampling", "2")[0], "mis or sunOnly");
    }

    [TestMethod]
    public void RouletteOption_SetsTheStart_WithinItsRange()
    {
        Assert.AreEqual(RenderSettings.DefaultRussianRouletteStart, Parse().RussianRouletteStart);
        Assert.AreEqual(3, Parse("--rr-start", "3").RussianRouletteStart);
        Assert.AreEqual(0, Parse("--rr-start", "0").RussianRouletteStart);
        StringAssert.Contains(Errors("--rr-start", "65")[0], "--rr-start");
        StringAssert.Contains(Errors("--rr-start", "-1")[0], "--rr-start");
    }

    [TestMethod]
    public void SheetAxis_VariesTheLightSampling()
    {
        Assert.IsTrue(SheetAxis.TryParse("lightSampling=sunOnly,mis", out SheetAxis? axis, out _));
        Assert.AreEqual(LightSampling.SunOnly, axis!.Apply(Parse(), 0).LightSampling);
        Assert.AreEqual(LightSampling.Mis, axis.Apply(Parse(), 1).LightSampling);
        Assert.IsFalse(SheetAxis.TryParse("lightSampling=mis,bsdf", out _, out string? error));
        StringAssert.Contains(error, "bsdf");
    }

    private PreviewRequest Request(params string[] extra)
    {
        File.WriteAllText(Path.Combine(_dir, "r.json"), """{ "setPieces": ["alpineRange"], "time": "goldenHour" }""");
        string[] args = ["--preview", "--recipe", Path.Combine(_dir, "r.json"), "--seed", "7", "--out", Path.Combine(_dir, "p.png"), "--width", "32", "--spp", "4", .. extra];
        Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" ", errors));
        return request! with { Grid = TestWorlds.Grid, Erosion = false };
    }

    [TestMethod]
    public void Pipeline_PassesTheEstimatorToTheRenderer_AndTheSidecarRecordsIt()
    {
        var renderer = new FakeRenderer();
        Sidecar mis = PreviewPipeline.Run(Request("--meter", "average"), renderer);
        Assert.AreEqual(LightSampling.Mis, renderer.Settings!.LightSampling);
        Assert.AreEqual(LightSampling.Mis, renderer.Meters.Single().LightSampling, "the average meter renders the same way");
        Assert.AreEqual(LightSampling.Mis, mis.Image.LightSampling);
        Assert.AreEqual(2, mis.Image.RussianRouletteStart);
        Assert.AreEqual(19, Sidecar.CurrentSchema);
        Assert.AreEqual(Sidecar.CurrentSchema, mis.Schema);

        Sidecar m14 = PreviewPipeline.Run(Request("--light-sampling", "sunOnly", "--rr-start", "3"), renderer);
        Assert.AreEqual(LightSampling.SunOnly, renderer.Settings!.LightSampling);
        Assert.AreEqual(3, renderer.Settings.RussianRouletteStart);
        Assert.AreEqual((LightSampling.SunOnly, 3), (m14.Image.LightSampling, m14.Image.RussianRouletteStart));

        JsonNode json = JsonNode.Parse(File.ReadAllText(Path.Combine(_dir, "p.json")))!;
        Assert.AreEqual("sunOnly", json["image"]!["lightSampling"]!.GetValue<string>());
        Assert.AreEqual(3, json["image"]!["russianRouletteStart"]!.GetValue<int>());
    }

    [TestMethod]
    public void ReferenceAtmosphere_IsRecordedAsSunOnly()
    {
        Sidecar reference = PreviewPipeline.Run(Request("--atmosphere", "reference"), new FakeRenderer());
        Assert.AreEqual(LightSampling.SunOnly, reference.Image.LightSampling, "the reference has no sky view to sample");
        Assert.AreEqual(LightSampling.SunOnly, ImageInfo.LightSamplingOf(new RenderSettings { Width = 1, Height = 1, Atmosphere = AtmosphereMode.ReferenceShadowed }));
        Assert.AreEqual(LightSampling.Mis, ImageInfo.LightSamplingOf(new RenderSettings { Width = 1, Height = 1 }));
    }

    [TestMethod]
    public void Labels_NameTheLightSampling_ButOnlyWhenItIsNotTheDefault()
    {
        JsonNode? Sidecar(string sampling) => JsonNode.Parse($$"""{ "seed": 1, "image": { "width": 320, "height": 180, "lightSampling": "{{sampling}}" } }""");
        IReadOnlyDictionary<string, string> mis = SheetLabels.Identity(Sidecar("mis"));
        IReadOnlyDictionary<string, string> sunOnly = SheetLabels.Identity(Sidecar("sunOnly"));
        Assert.AreEqual("mis", mis["image.lightSampling"]);
        IReadOnlyList<string> labels = SheetLabels.Differences([sunOnly, mis]);
        Assert.AreEqual("lights=sunOnly", labels[0]);
        Assert.AreEqual("lights=mis", labels[1]);
        Assert.IsFalse(SheetLabels.Title([mis, mis]).Contains("light sampling", StringComparison.Ordinal));
        StringAssert.Contains(SheetLabels.Title([sunOnly, sunOnly]), "sunOnly light sampling");
    }
}
