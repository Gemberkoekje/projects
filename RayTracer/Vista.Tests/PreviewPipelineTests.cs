using System.Text.Json;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>
/// The headless preview end to end, minus the GPU (vista-plan §7.4): a dry run, and a full run against a
/// fake renderer that honours the <see cref="IVistaRenderer"/> contract, so every file the real renderer
/// will produce is written and checked here.
/// </summary>
[TestClass]
public sealed class PreviewPipelineTests
{
    private string _dir = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "r.json"), """{ "terrain": ["alpineRange"], "time": "goldenHour" }""");
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    private PreviewRequest Request(bool dryRun, params DebugOutput[] debug) => new()
    {
        RecipePath = Path.Combine(_dir, "r.json"),
        Seed = 7,
        OutPath = Path.Combine(_dir, "p.png"),
        Width = 32,
        Height = 18,
        SamplesPerPixel = 4,
        Debug = debug.ToHashSet(),
        DryRun = dryRun,
    };

    [TestMethod]
    public void DryRun_WritesSidecarAndHeightMapOnly()
    {
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: true), renderer: null);

        Assert.AreEqual("dryRun", sidecar.Kind);
        Assert.IsNull(sidecar.Render);
        Assert.IsNull(sidecar.Engine.Device);
        Assert.IsTrue(sidecar.DefaultCamera);
        Assert.AreEqual("mountains", sidecar.World.TerrainProfile);
        Assert.AreEqual(1025, sidecar.World.GridSize);
        Assert.AreEqual(DataTier.BuiltIn, sidecar.DataTiers["terrain"]);
        CollectionAssert.AreEquivalent(new[] { "heightMap", "sidecar" }, sidecar.Outputs.Keys.ToArray());
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "p.heightmap.png")));
        Assert.IsFalse(File.Exists(Path.Combine(_dir, "p.png")));
        Assert.IsTrue(sidecar.TimingsSeconds.ContainsKey("worldGen"));
    }

    [TestMethod]
    public void DryRun_IsReproducible()
    {
        Sidecar a = PreviewPipeline.Run(Request(dryRun: true), renderer: null);
        Sidecar b = PreviewPipeline.Run(Request(dryRun: true), renderer: null);
        Assert.AreEqual(a.World, b.World);
        Assert.AreEqual(a.Camera, b.Camera);
        CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(_dir, "p.heightmap.png")), PngEncoder.Encode(
            DebugImages.HeightMap(World.TerrainGenerator.Generate(a.Recipe, 7), a.Camera).Rgb, 512, 512, PngColor.Rgb));
    }

    [TestMethod]
    public void Sidecar_RoundTripsThroughTheStrictDialect()
    {
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: false, DebugOutput.Depth), new FakeRenderer());
        string json = File.ReadAllText(Path.Combine(_dir, "p.json"));
        Sidecar back = VistaJson.Deserialize<Sidecar>(json);
        Assert.AreEqual(sidecar.World, back.World);
        Assert.AreEqual(sidecar.Camera, back.Camera);
        Assert.IsTrue(sidecar.Recipe.ContentEquals(back.Recipe));
        Assert.AreEqual(sidecar.Render!.Stats.RaysTraced, back.Render!.Stats.RaysTraced);
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.AreEqual("builtIn", doc.RootElement.GetProperty("dataTiers").GetProperty("terrain").GetString());
        Assert.AreEqual(7ul, doc.RootElement.GetProperty("seed").GetUInt64());
    }

    [TestMethod]
    public void Render_WritesBeautyExrDebugAndSidecar()
    {
        var renderer = new FakeRenderer();
        PreviewRequest request = Request(dryRun: false, Enum.GetValues<DebugOutput>());
        var time = new FixedTime(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
        Sidecar sidecar = PreviewPipeline.Run(request, renderer, time);

        Assert.AreEqual("preview", sidecar.Kind);
        Assert.AreEqual("2026-10-18T12:00:00.0000000Z", sidecar.CreatedUtc);
        Assert.AreEqual(FakeRenderer.Name, sidecar.Engine.Device);
        Assert.AreEqual(32 * 18 * 4L, sidecar.Render!.Stats.PixelSamples);
        Assert.AreEqual(3.0, sidecar.Render.RaysPerSample, 1e-12);
        Assert.IsTrue(renderer.Planner!.IsComplete, "the renderer drove the planner to completion");
        CollectionAssert.AreEquivalent(new[] { Aov.Albedo, Aov.Normal, Aov.Depth, Aov.MaterialId, Aov.Variance }, renderer.Requested!.ToArray());

        foreach (string file in sidecar.Outputs.Values)
            Assert.IsTrue(File.Exists(Path.Combine(_dir, file)), file);
        CollectionAssert.AreEquivalent(
            new[] { "albedo", "beauty", "beautyExr", "depth", "material", "normal", "sidecar", "tier", "variance" },
            sidecar.Outputs.Keys.ToArray());
        byte[] png = File.ReadAllBytes(Path.Combine(_dir, "p.png"));
        Assert.AreEqual(0x89, png[0]);
        Assert.AreEqual(20000630, BitConverter.ToInt32(File.ReadAllBytes(Path.Combine(_dir, "p.exr")), 0));
    }

    [TestMethod]
    public void Render_PassesTheSuppliedCamera()
    {
        string cameraPath = Path.Combine(_dir, "c.json");
        CameraSpec camera = new() { Position = new WorldPoint(100, 5000, -2000), YawDegrees = 45, PitchDegrees = -10 };
        File.WriteAllText(cameraPath, VistaJson.Serialize(camera));
        var renderer = new FakeRenderer();
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: false) with { CameraPath = cameraPath }, renderer);
        Assert.AreEqual(camera, renderer.Scene!.Camera);
        Assert.IsFalse(sidecar.DefaultCamera);
        Assert.AreEqual(0, sidecar.Warnings.Count);
    }

    [TestMethod]
    public void UndergroundCamera_IsAWarningNotAnError()
    {
        string cameraPath = Path.Combine(_dir, "c.json");
        File.WriteAllText(cameraPath, VistaJson.Serialize(new CameraSpec { Position = new WorldPoint(0, -500, 0) }));
        Sidecar sidecar = PreviewPipeline.Run(Request(dryRun: true) with { CameraPath = cameraPath }, renderer: null);
        StringAssert.Contains(sidecar.Warnings.Single(), "below the terrain");
    }

    [TestMethod]
    public void MissingAov_FailsLoudly()
    {
        var e = Assert.ThrowsExactly<InvalidOperationException>(() =>
            PreviewPipeline.Run(Request(dryRun: false, DebugOutput.Normal), new FakeRenderer { Skip = Aov.Normal }));
        StringAssert.Contains(e.Message, "Normal");
    }

    [TestMethod]
    public void NoRenderer_RequiresADryRun() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => PreviewPipeline.Run(Request(dryRun: false), renderer: null));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A stand-in for the GPU: drives the planner like the real host must, returns synthetic buffers.</summary>
    private sealed class FakeRenderer : IVistaRenderer
    {
        public const string Name = "Fake GPU";

        public Aov? Skip { get; init; }

        public BatchPlanner? Planner { get; private set; }

        public IReadOnlySet<Aov>? Requested { get; private set; }

        public VistaScene? Scene { get; private set; }

        public string DeviceName => Name;

        public RenderOutput Render(VistaScene scene, RenderSettings settings, IReadOnlySet<Aov> aovs, BatchPlanner planner)
        {
            Scene = scene;
            Planner = planner;
            Requested = aovs;
            int dispatches = 0;
            while (planner.HasNext)
            {
                SampleBatch batch = planner.Next();
                planner.Complete(batch, TimeSpan.FromMilliseconds(1));
                dispatches++;
            }

            int pixels = settings.Width * settings.Height;
            var xyz = new float[pixels * 3];
            for (int p = 0; p < pixels; p++)
            {
                float t = p / (float)pixels;
                xyz[p * 3] = 0.95f * t;
                xyz[p * 3 + 1] = t;
                xyz[p * 3 + 2] = 1.09f * t;
            }
            var buffers = new Dictionary<Aov, float[]>();
            foreach (Aov aov in aovs.Where(a => a != Skip))
            {
                var data = new float[pixels * AovLayout.Channels(aov)];
                for (int i = 0; i < data.Length; i++)
                    data[i] = (i % 7) * 0.1f + 0.05f;
                if (aov == Aov.MaterialId)
                {
                    for (int i = 1; i < data.Length; i += 2)
                        data[i] = -1f; // every other pixel is sky
                    for (int i = 0; i < data.Length; i += 2)
                        data[i] = 0f;  // the rest is terrain
                }
                buffers[aov] = data;
            }
            long pixelSamples = (long)pixels * settings.SamplesPerPixel;
            return new RenderOutput(settings.Width, settings.Height, xyz, buffers, new RenderStats
            {
                RaysTraced = (ulong)(pixelSamples * 3),
                PixelSamples = pixelSamples,
                Dispatches = dispatches,
                TraceSeconds = dispatches * 0.001,
            });
        }
    }
}
