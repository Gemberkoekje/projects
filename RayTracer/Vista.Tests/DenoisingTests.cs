using System.Text.Json;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;

namespace Vista.Tests;

/// <summary>
/// M1.6's seam (vista-plan §10 M1.6): the denoise settings and their measured defaults, the input the denoiser is given, the
/// <c>--denoise</c> options, and the pipeline and sheets with a fake denoiser — the denoised files beside the raw ones, the raw
/// ones unchanged, the schema-7 sidecar, and a denoiser that cannot run leaving a warning and the raw files only.
/// </summary>
[TestClass]
public sealed class DenoisingTests
{
    private string _dir = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(P("r.json"), """{ "name": "test range", "setPieces": ["alpineRange"], "time": "goldenHour" }""");
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    private string P(string name) => Path.Combine(_dir, name);

    private PreviewRequest Request(string outName = "p.png", DenoiseSettings? denoise = null) => new()
    {
        RecipePath = P("r.json"),
        Seed = 7,
        OutPath = P(outName),
        Width = 32,
        Height = 18,
        SamplesPerPixel = 4,
        Denoise = denoise,
        Grid = TestWorlds.Grid,
        Erosion = false,
    };

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

    private static ExposureInfo Exposure(double logAverageY) => new(Metering.Average, AutoExposure.MethodName, 0.0, 3.0, 10.0, 3.0)
    {
        Average = new AverageReading(logAverageY, logAverageY * IntegratorTables.LuminancePerY),
    };

    [TestMethod]
    public void Defaults_AreTheMeasuredChoice()
    {
        var settings = new DenoiseSettings();
        Assert.AreEqual(DenoiseDevice.Cpu, settings.Device);
        Assert.AreEqual(DenoiseQuality.High, settings.Quality);
        Assert.AreEqual(DenoiseAuxiliary.Clean, settings.Auxiliary);
        Assert.AreEqual(DenoiseInputScale.Metered, settings.InputScale);
    }

    [TestMethod]
    public void InputScale_IsAutomaticPhysicalOrTheMeters()
    {
        ExposureInfo exposure = Exposure(0.02);
        var settings = new DenoiseSettings();
        Assert.IsTrue(float.IsNaN(Denoising.InputScale(settings with { InputScale = DenoiseInputScale.Auto }, exposure)));
        Assert.AreEqual((float)(IntegratorTables.LuminancePerY / 100.0), Denoising.InputScale(settings with { InputScale = DenoiseInputScale.Physical }, exposure));
        Assert.AreEqual(729.83, Denoising.InputScale(settings with { InputScale = DenoiseInputScale.Physical }, exposure), 0.01, "1 is 100 cd/m²");
        Assert.AreEqual(9f, Denoising.InputScale(settings, exposure), 1e-5f, "the meter's mean maps to 0.18");
        Assert.IsTrue(float.IsNaN(Denoising.InputScale(settings, Exposure(0.0))), "no light metered: the filter chooses");
        var card = new ExposureInfo(Metering.Incident, IncidentMeter.MethodName, 1.0, 2.0, 12.0, 3.0)
        {
            Incident = new IncidentReading(5.0, 2000.0, Math.PI / 4.0, 1.0, 0.5, 0.5),
        };
        Assert.AreEqual(4f, Denoising.InputScale(settings, card), 1e-6f, "the grey card maps to 0.18: π / E, the compensation left out");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Denoising.InputScale(settings with { InputScale = DenoiseInputScale.None }, exposure));
    }

    [TestMethod]
    public void Prepare_GivesTheFilterOnlyWhatItCanTake_AndCopies()
    {
        float[] albedo = [1.5f, -0.1f, 0.5f, float.NaN, 0.2f, 1f];
        float[] normal = [2f, -3f, 0.5f, float.NaN, 0f, -1f];
        var output = new RenderOutput(2, 1, new float[6], new Dictionary<Aov, float[]> { [Aov.Albedo] = albedo, [Aov.Normal] = normal }, new RenderStats());
        float[] linear = [1f, -0.5f, float.NaN, 2f, float.PositiveInfinity, 0.25f];

        DenoiseInput input = Denoising.Prepare(output, linear, 4f, out long clamped);

        CollectionAssert.AreEqual(new[] { 1f, 0f, 0f, 2f, 0f, 0.25f }, input.Color);
        CollectionAssert.AreEqual(new[] { 1f, 0f, 0.5f, 0f, 0.2f, 1f }, input.Albedo);
        CollectionAssert.AreEqual(new[] { 1f, -1f, 0.5f, 0f, 0f, -1f }, input.Normal);
        Assert.AreEqual(3, clamped);
        Assert.AreEqual((2, 1, 2, 4f), (input.Width, input.Height, input.Pixels, input.InputScale));
        Assert.AreEqual(1.5f, albedo[0], "the render's buffers are not changed");
        Assert.AreEqual(-0.5f, linear[1]);
    }

    [TestMethod]
    public void Prepare_NeedsTheAlbedoAndNormal_OfTheImagesSize()
    {
        var noNormal = new RenderOutput(1, 1, new float[3], new Dictionary<Aov, float[]> { [Aov.Albedo] = new float[3] }, new RenderStats());
        StringAssert.Contains(Assert.ThrowsExactly<InvalidOperationException>(() => Denoising.Prepare(noNormal, new float[3], 1f, out _)).Message, "Normal");
        var shortAlbedo = new RenderOutput(1, 1, new float[3], new Dictionary<Aov, float[]> { [Aov.Albedo] = new float[2], [Aov.Normal] = new float[3] }, new RenderStats());
        Assert.ThrowsExactly<InvalidOperationException>(() => Denoising.Prepare(shortAlbedo, new float[3], 1f, out _));
        Assert.ThrowsExactly<ArgumentException>(() => Denoising.Prepare(noNormal, new float[6], 1f, out _));
    }

    [TestMethod]
    public void Result_IsAnImageOrAProblem()
    {
        var run = new DenoiserRun("lib", "1", "CPU: x", "RT", null, 0, 0);
        DenoiseResult denoised = DenoiseResult.Denoised([1f, 2f, 3f], run);
        Assert.IsTrue(denoised.Succeeded);
        Assert.AreEqual(string.Empty, denoised.Problem);
        Assert.AreSame(run, denoised.Run);

        DenoiseResult failed = DenoiseResult.Failed("No library.");
        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual(0, failed.Color.Length);
        Assert.IsNull(failed.Run);
        Assert.ThrowsExactly<ArgumentException>(() => DenoiseResult.Failed(" "));
    }

    [TestMethod]
    public void DenoiseOption_TurnsDenoisingOn_OnTheCpuOrCuda()
    {
        Assert.IsNull(Parse().Denoise);
        Assert.AreEqual(new DenoiseSettings(), Parse("--denoise").Denoise);
        Assert.AreEqual(DenoiseDevice.Cuda, Parse("--denoise", "--denoise-device", "cuda").Denoise!.Device);
        Assert.AreEqual(DenoiseDevice.Cpu, Parse("--denoise-device", "CPU", "--denoise").Denoise!.Device);
        StringAssert.Contains(PreviewCommandLine.Usage, "--denoise");
    }

    [TestMethod]
    public void DenoiseOptions_AreChecked()
    {
        CollectionAssert.Contains(Errors("--denoise-device", "cuda").ToList(), "--denoise-device needs --denoise.");
        CollectionAssert.Contains(Errors("--denoise", "--dry-run").ToList(), "--denoise needs a render, and --dry-run renders nothing.");
        StringAssert.Contains(Errors("--denoise", "--denoise-device", "gpu").Single(), "--denoise-device must be cpu or cuda");
        StringAssert.Contains(Errors("--denoise", "--denoise-device", "1").Single(), "--denoise-device must be cpu or cuda");
        StringAssert.Contains(Errors("--denoise", "--denoise-device", "none").Single(), "--denoise-device must be cpu or cuda");
        CollectionAssert.Contains(Errors("--denoise", "--denoise").ToList(), "--denoise is given more than once.");
    }

    [TestMethod]
    public void Sheets_TakeTheDenoiseOption_SheetFromDoesNot()
    {
        Assert.IsTrue(SheetCommandLine.TryParse(["--sheet", "--vary", "seed=1,2", "--recipe", "r.json", "--out", "s.png", "--denoise"],
            out SheetRequest? sheet, out IReadOnlyList<string> errors), string.Join(" ", errors));
        Assert.AreEqual(new DenoiseSettings(), sheet!.Base.Denoise);

        Assert.IsFalse(SheetCommandLine.TryParseFrom(["--sheet-from", "a.png", "--out", "s.png", "--denoise"], out _, out errors));
        CollectionAssert.Contains(errors.ToList(), "--denoise does not apply to --sheet-from.");
    }

    [TestMethod]
    public void Pipeline_WritesTheDenoisedBeautyBesideTheRawOne()
    {
        var renderer = new FakeRenderer();
        var denoiser = new FakeDenoiser();
        Sidecar sidecar = PreviewPipeline.Run(Request(denoise: new DenoiseSettings()), renderer, denoiser: denoiser);

        CollectionAssert.AreEquivalent(new[] { Aov.Albedo, Aov.Normal }, renderer.Requested!.ToArray(), "the denoiser's inputs, though no debug image was asked for");
        DenoiseInput input = denoiser.Inputs.Single();
        Assert.AreEqual((32, 18, 32 * 18 * 3), (input.Width, input.Height, input.Color.Length));
        Assert.AreEqual((float)(Math.PI / sidecar.Exposure!.Incident!.IlluminanceY), input.InputScale, "the incident meter's grey card");
        Assert.AreEqual(new DenoiseSettings(), denoiser.Settings.Single());

        CollectionAssert.AreEquivalent(new[] { "beauty", "beautyExr", "denoised", "denoisedExr", "sidecar" }, sidecar.Outputs.Keys.ToArray());
        Assert.AreEqual("p.denoised.png", sidecar.Outputs["denoised"]);
        Assert.AreEqual("p.denoised.exr", sidecar.Outputs["denoisedExr"]);
        foreach (string file in sidecar.Outputs.Values)
            Assert.IsTrue(File.Exists(P(file)), file);
        float[] halved = [.. input.Color.Select(c => 0.5f * c)];
        CollectionAssert.AreEqual(PngEncoder.Encode(DisplayTransform.ToSrgb8(halved, sidecar.Exposure.AppliedEv, DisplayCurve.Agx), 32, 18, PngColor.Rgb),
            File.ReadAllBytes(P("p.denoised.png")), "the denoised PNG is exposed like the raw one");
        CollectionAssert.AreEqual(ExrEncoder.EncodeRgb(halved, 32, 18), File.ReadAllBytes(P("p.denoised.exr")));

        DenoiseInfo info = sidecar.Denoise!;
        Assert.AreEqual(new DenoiseSettings(), info.Settings);
        Assert.AreEqual(FakeDenoiser.Library, info.Run!.Library);
        Assert.IsNull(info.Problem);
        Assert.AreEqual(DenoiseInfo.InputsDescription, info.Inputs);
        Assert.IsTrue(sidecar.TimingsSeconds.ContainsKey("denoise"));
        Assert.AreEqual(0, sidecar.Warnings.Count);
    }

    [TestMethod]
    public void Pipeline_RawFilesAreTheSame_WithOrWithoutDenoising()
    {
        PreviewPipeline.Run(Request("plain.png"), new FakeRenderer());
        PreviewPipeline.Run(Request("denoised.png", new DenoiseSettings()), new FakeRenderer(), denoiser: new FakeDenoiser());
        CollectionAssert.AreEqual(File.ReadAllBytes(P("plain.png")), File.ReadAllBytes(P("denoised.png")));
        CollectionAssert.AreEqual(File.ReadAllBytes(P("plain.exr")), File.ReadAllBytes(P("denoised.exr")));
    }

    [TestMethod]
    public void Pipeline_ADenoiserThatCannotRun_LeavesAWarningAndTheRawFiles()
    {
        Sidecar sidecar = PreviewPipeline.Run(Request(denoise: new DenoiseSettings { Device = DenoiseDevice.Cuda }), new FakeRenderer(),
            denoiser: new FakeDenoiser { Failure = "No library here." });

        Assert.AreEqual("Not denoised: No library here. Wrote the raw image only.", sidecar.Warnings.Single());
        CollectionAssert.AreEquivalent(new[] { "beauty", "beautyExr", "sidecar" }, sidecar.Outputs.Keys.ToArray());
        Assert.IsFalse(File.Exists(P("p.denoised.png")));
        Assert.IsTrue(File.Exists(P("p.png")));
        Assert.AreEqual("No library here.", sidecar.Denoise!.Problem);
        Assert.IsNull(sidecar.Denoise.Run);
        Assert.AreEqual(DenoiseDevice.Cuda, sidecar.Denoise.Settings.Device);
    }

    [TestMethod]
    public void Pipeline_DenoisingNeedsADenoiser_AndNotDenoisingRecordsNothing()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => PreviewPipeline.Run(Request(denoise: new DenoiseSettings()), new FakeRenderer()));
        Sidecar plain = PreviewPipeline.Run(Request(), new FakeRenderer(), denoiser: new FakeDenoiser());
        Assert.IsNull(plain.Denoise);
        Assert.IsFalse(plain.Outputs.ContainsKey("denoised"));
    }

    [TestMethod]
    public void Pipeline_ALoupeIsDenoisedAtItsOwnSize()
    {
        var denoiser = new FakeDenoiser();
        PreviewPipeline.Run(Request(denoise: new DenoiseSettings()) with { Loupe = new PixelWindow(5, 3, 8, 6) }, new FakeRenderer(), denoiser: denoiser);
        Assert.AreEqual((8, 6), (denoiser.Inputs.Single().Width, denoiser.Inputs.Single().Height));
        Assert.AreEqual((8, 6), (PngDecoder.Read(P("p.denoised.png")).Width, PngDecoder.Read(P("p.denoised.png")).Height));
    }

    [TestMethod]
    public void Sidecar_RecordsTheDenoising_AndReadsBack()
    {
        Sidecar sidecar = PreviewPipeline.Run(Request(denoise: new DenoiseSettings()), new FakeRenderer(), denoiser: new FakeDenoiser());
        string json = File.ReadAllText(P("p.json"));
        Sidecar back = VistaJson.Deserialize<Sidecar>(json);
        Assert.AreEqual(sidecar.Denoise, back.Denoise);
        Assert.AreEqual(13, back.Schema);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement denoise = doc.RootElement.GetProperty("denoise");
        Assert.AreEqual("metered", denoise.GetProperty("settings").GetProperty("inputScale").GetString());
        Assert.AreEqual("clean", denoise.GetProperty("settings").GetProperty("auxiliary").GetString());
        Assert.AreEqual(FakeDenoiser.Library, denoise.GetProperty("run").GetProperty("library").GetString());
        Assert.AreEqual("1.0.0", denoise.GetProperty("run").GetProperty("version").GetString());
    }

    [TestMethod]
    public void Sheet_WithDenoising_AlsoTilesTheDenoisedCells()
    {
        Assert.IsTrue(SheetAxis.TryParse("seed=7,8", out SheetAxis? axis, out string? error), error);
        var request = new SheetRequest { Axis = axis!, Base = Request("sheet.png", new DenoiseSettings()) };
        var denoiser = new FakeDenoiser();
        SheetSidecar sheet = SheetPipeline.Render(request, new FakeRenderer(), denoiser: denoiser);

        Assert.AreEqual(2, denoiser.Inputs.Count);
        Assert.AreEqual("sheet.denoised.png", sheet.Outputs["denoised"]);
        PngImage raw = PngDecoder.Read(P("sheet.png"));
        PngImage denoised = PngDecoder.Read(P("sheet.denoised.png"));
        Assert.AreEqual(raw.Width, denoised.Width, "the same cells (its title, which says denoised, may wrap onto another line)");
        CollectionAssert.AreNotEqual(raw.Pixels, denoised.Pixels);

        SheetSidecar plain = SheetPipeline.Render(request with { Base = Request("plain.png") }, new FakeRenderer());
        Assert.IsFalse(plain.Outputs.ContainsKey("denoised"));
        SheetSidecar failed = SheetPipeline.Render(request with { Base = Request("failed.png", new DenoiseSettings()) }, new FakeRenderer(),
            denoiser: new FakeDenoiser { Failure = "No library here." });
        Assert.IsFalse(failed.Outputs.ContainsKey("denoised"), "a sheet of denoised cells needs every cell denoised");
        CollectionAssert.AreEqual(new[] { "seed=7: Not denoised", "seed=8: Not denoised" }, failed.Warnings.Select(w => w[..20]).ToArray());
    }

    [TestMethod]
    public void SheetFrom_TellsTheRawAndTheDenoisedImageOfOneRenderApart()
    {
        PreviewPipeline.Run(Request(denoise: new DenoiseSettings()), new FakeRenderer(), denoiser: new FakeDenoiser());
        SheetSidecar sheet = SheetPipeline.FromFiles(new SheetFromRequest { Inputs = [P("p.png"), P("p.denoised.png")], OutPath = P("pair.png") });
        CollectionAssert.AreEqual(new[] { "output=beauty", "output=denoised" }, sheet.Cells.Select(c => c.Label).ToArray());
        Assert.IsFalse(sheet.Title.Contains("beauty", StringComparison.Ordinal), sheet.Title);

        SheetSidecar both = SheetPipeline.FromFiles(new SheetFromRequest { Inputs = [P("p.denoised.png"), P("p.denoised.png")], OutPath = P("same.png") });
        StringAssert.Contains(both.Title, "denoised", "a role every cell shares goes in the title, unless it is the beauty");
    }
}
