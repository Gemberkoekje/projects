using System.Text.Json.Nodes;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;

namespace Vista.Tests;

/// <summary>
/// Contact sheets end to end, minus the GPU (vista-plan §7.4): variants rendered through the fake renderer,
/// and existing previews tiled from their files.
/// </summary>
[TestClass]
public sealed class SheetPipelineTests
{
    private string _dir = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-sheet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "r.json"), """{ "name": "test range", "setPieces": ["alpineRange"], "time": "goldenHour" }""");
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    private string P(string name) => Path.Combine(_dir, name);

    private SheetRequest Request(string vary, bool dryRun = false, params DebugOutput[] debug)
    {
        Assert.IsTrue(SheetAxis.TryParse(vary, out SheetAxis? axis, out string? error), error);
        return new SheetRequest
        {
            Axis = axis!,
            Base = new PreviewRequest
            {
                RecipePath = P("r.json"),
                Seed = 7,
                OutPath = P("sheet.png"),
                Width = 32,
                Height = 18,
                SamplesPerPixel = 4,
                Debug = debug.ToHashSet(),
                DryRun = dryRun,
            },
        };
    }

    [TestMethod]
    public void SeedSheet_RendersEveryCellAndListsItInOneSidecar()
    {
        var renderer = new FakeRenderer();
        SheetSidecar sheet = SheetPipeline.Render(Request("seed=7,8") with { Title = "two seeds" }, renderer);

        Assert.AreEqual(("sheet", "seed", "two seeds"), (sheet.Kind, sheet.Axis, sheet.Title));
        Assert.AreEqual(FakeRenderer.Name, sheet.Engine.Device);
        Assert.AreEqual((2, 1), (sheet.Columns, sheet.Rows));
        CollectionAssert.AreEqual(new ulong[] { 7, 8 }, renderer.Scenes.Select(s => s.Seed).ToArray());
        CollectionAssert.AreEqual(new[] { "seed=7", "seed=8" }, sheet.Cells.Select(c => c.Label).ToArray());
        CollectionAssert.AreEqual(new[] { "sheet.cells/cell01.png", "sheet.cells/cell02.png" }, sheet.Cells.Select(c => c.Source).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, sheet.Cells.Select(c => c.Index).ToArray());
        Assert.AreEqual(8ul, (ulong)sheet.Cells[1].Sidecar!["seed"]!, "each cell's own sidecar is embedded");
        CollectionAssert.AreEquivalent(new[] { "sheet", "sidecar" }, sheet.Outputs.Keys.ToArray());

        PngImage png = PngDecoder.Read(P("sheet.png"));
        Assert.AreEqual((sheet.Width, sheet.Height), (png.Width, png.Height));
        foreach (string file in new[] { "cell01.png", "cell01.exr", "cell01.json", "cell02.png" })
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "sheet.cells", file)), file);

        SheetSidecar back = VistaJson.Deserialize<SheetSidecar>(File.ReadAllText(P("sheet.json")));
        Assert.AreEqual(sheet.Cells.Count, back.Cells.Count);
        Assert.AreEqual("seed=8", back.Cells[1].Label);
        Assert.AreEqual(8ul, (ulong)back.Cells[1].Sidecar!["seed"]!);
    }

    [TestMethod]
    public void Tiles_AreTheCellsPixels()
    {
        SheetSidecar sheet = SheetPipeline.Render(Request("seed=7,8"), new FakeRenderer());
        PngImage png = PngDecoder.Read(P("sheet.png"));
        PngImage cell = PngDecoder.Read(Path.Combine(_dir, "sheet.cells", "cell02.png"));
        SheetCellInfo second = sheet.Cells[1];
        for (int y = 0; y < cell.Height; y++)
        {
            for (int x = 0; x < cell.Width * 3; x++)
                Assert.AreEqual(cell.Pixels[y * cell.Width * 3 + x], png.Pixels[((second.Y + y) * png.Width + second.X) * 3 + x]);
        }
    }

    [TestMethod]
    public void TimeSheet_SharesOneWorldAndTitlesWhatTheCellsShare()
    {
        var renderer = new FakeRenderer();
        SheetSidecar sheet = SheetPipeline.Render(Request("time=dawn,dusk,night"), renderer);

        CollectionAssert.AreEqual(new[] { TimeOfDay.Dawn, TimeOfDay.Dusk, TimeOfDay.Night }, renderer.Scenes.Select(s => s.Recipe.Time).ToArray());
        Assert.AreSame(renderer.Scenes[0].Mesh, renderer.Scenes[2].Mesh, "the world is generated and meshed once");
        Assert.AreEqual((3, 1), (sheet.Columns, sheet.Rows));
        StringAssert.StartsWith(sheet.Title, "test range · alpineRange · summer · seed 7 · default camera · 32×18 · 4 spp");
        Assert.IsFalse(sheet.Title.Contains("dawn", StringComparison.Ordinal), "what varies is in the labels, not the title");
    }

    [TestMethod]
    public void DebugOutputs_GetASheetEach()
    {
        SheetSidecar sheet = SheetPipeline.Render(Request("seed=7,8", dryRun: false, DebugOutput.Normal, DebugOutput.Depth), new FakeRenderer());
        CollectionAssert.AreEquivalent(new[] { "depth", "normal", "sheet", "sidecar" }, sheet.Outputs.Keys.ToArray());
        PngImage normals = PngDecoder.Read(P("sheet.normal.png"));
        Assert.AreEqual((sheet.Width, sheet.Height), (normals.Width, normals.Height));
    }

    [TestMethod]
    public void LoupeSheet_TilesTheWindows()
    {
        SheetRequest request = Request("spp=1,4");
        SheetSidecar sheet = SheetPipeline.Render(request with { Base = request.Base with { Loupe = new Rendering.PixelWindow(4, 2, 10, 6) } },
            new FakeRenderer());
        Assert.IsTrue(sheet.Cells.All(c => (c.Width, c.Height) == (10, 6)));
        StringAssert.Contains(sheet.Title, "loupe 4,2,10,6");
    }

    [TestMethod]
    public void DryRunSheet_TilesHeightMaps()
    {
        SheetSidecar sheet = SheetPipeline.Render(Request("seed=7,8", dryRun: true), renderer: null);
        CollectionAssert.AreEqual(new[] { "sheet.cells/cell01.heightmap.png", "sheet.cells/cell02.heightmap.png" },
            sheet.Cells.Select(c => c.Source).ToArray());
        Assert.IsNull(sheet.Engine.Device);
        Assert.AreEqual((512, 512), (sheet.Cells[0].Width, sheet.Cells[0].Height));
    }

    [TestMethod]
    public void AMisspeltField_FailsBeforeAnythingRenders()
    {
        var renderer = new FakeRenderer();
        var e = Assert.ThrowsExactly<InvalidDataException>(() => SheetPipeline.Render(Request("tme=dawn,dusk"), renderer));
        StringAssert.Contains(e.Message, "tme=dawn");
        Assert.AreEqual(0, renderer.Scenes.Count);
        Assert.IsFalse(Directory.Exists(P("sheet.cells")));

        Assert.ThrowsExactly<InvalidDataException>(() => SheetPipeline.Render(Request("camera.yaw=0,90"), renderer));
        Assert.ThrowsExactly<InvalidDataException>(() => SheetPipeline.Render(Request("camera=missing.json"), renderer));
        Assert.AreEqual(0, renderer.Scenes.Count);
    }

    [TestMethod]
    public void NoRenderer_RequiresADryRun() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => SheetPipeline.Render(Request("seed=1"), renderer: null));

    [TestMethod]
    public void SheetFrom_LabelsEachImageByWhatItsSidecarSaysThatTheOthersDoNot()
    {
        var renderer = new FakeRenderer();
        PreviewRequest preview = Request("seed=7").Base;
        PreviewPipeline.Run(preview with { OutPath = P("a.png") }, renderer);
        PreviewPipeline.Run(preview with { OutPath = P("b.png"), Seed = 8 }, renderer);

        SheetSidecar sheet = SheetPipeline.FromFiles(new SheetFromRequest { Inputs = [P("a.png"), P("b.png")], OutPath = P("ab.png") });
        Assert.AreEqual("sheetFrom", sheet.Kind);
        Assert.IsNull(sheet.Axis);
        CollectionAssert.AreEqual(new[] { "seed=7", "seed=8" }, sheet.Cells.Select(c => c.Label).ToArray());
        StringAssert.StartsWith(sheet.Title, "test range · alpineRange · goldenHour · summer · default camera");
        CollectionAssert.AreEqual(new[] { "a.png", "b.png" }, sheet.Cells.Select(c => c.Source).ToArray());
        Assert.AreEqual(8ul, (ulong)sheet.Cells[1].Sidecar!["seed"]!);
        Assert.AreEqual(0, sheet.Warnings.Count);
        Assert.IsTrue(File.Exists(P("ab.json")));
    }

    [TestMethod]
    public void SheetFrom_DebugImagesFindTheirPreviewsSidecar()
    {
        PreviewRequest preview = Request("seed=7").Base with { Debug = new HashSet<DebugOutput> { DebugOutput.Normal } };
        PreviewPipeline.Run(preview with { OutPath = P("a.png") }, new FakeRenderer());
        PreviewPipeline.Run(preview with { OutPath = P("b.png"), SamplesPerPixel = 16 }, new FakeRenderer());
        SheetSidecar sheet = SheetPipeline.FromFiles(new SheetFromRequest { Inputs = [P("a.normal.png"), P("b.normal.png")], OutPath = P("n.png") });
        CollectionAssert.AreEqual(new[] { "spp=4", "spp=16" }, sheet.Cells.Select(c => c.Label).ToArray());
    }

    [TestMethod]
    public void SheetFrom_ExplicitLabelsAndMissingSidecars()
    {
        PreviewPipeline.Run(Request("seed=7").Base with { OutPath = P("a.png") }, new FakeRenderer());
        File.Copy(P("a.png"), P("loose.png"));

        SheetSidecar labelled = SheetPipeline.FromFiles(new SheetFromRequest
        {
            Inputs = [P("a.png"), P("loose.png")],
            OutPath = P("l.png"),
            Labels = ["before", "after"],
            Title = "by hand",
        });
        CollectionAssert.AreEqual(new[] { "before", "after" }, labelled.Cells.Select(c => c.Label).ToArray());
        Assert.AreEqual("by hand", labelled.Title);

        SheetSidecar unlabelled = SheetPipeline.FromFiles(new SheetFromRequest { Inputs = [P("a.png"), P("loose.png")], OutPath = P("u.png") });
        Assert.AreEqual("loose.png", unlabelled.Cells[1].Label);
        Assert.IsNull(unlabelled.Cells[1].Sidecar);
        StringAssert.Contains(unlabelled.Warnings.Single(), "no sidecar");

        Assert.ThrowsExactly<FileNotFoundException>(() =>
            SheetPipeline.FromFiles(new SheetFromRequest { Inputs = [P("nope.png")], OutPath = P("x.png") }));
    }

    [TestMethod]
    public void SheetFrom_IdenticalRendersFallBackToFileNames()
    {
        PreviewRequest preview = Request("seed=7").Base;
        PreviewPipeline.Run(preview with { OutPath = P("a.png") }, new FakeRenderer());
        PreviewPipeline.Run(preview with { OutPath = P("b.png") }, new FakeRenderer());
        SheetSidecar sheet = SheetPipeline.FromFiles(new SheetFromRequest { Inputs = [P("a.png"), P("b.png")], OutPath = P("s.png") });
        CollectionAssert.AreEqual(new[] { "a.png", "b.png" }, sheet.Cells.Select(c => c.Label).ToArray());
        Assert.IsInstanceOfType<JsonObject>(sheet.Cells[0].Sidecar);
    }
}
