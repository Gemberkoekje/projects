using System.Diagnostics;
using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class Phase5PerformanceTests
{
    private static JobSystem CreateJobSystem(
        int width = 16,
        int height = 16,
        DenoiseOptions? denoise = null)
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(2, 2, seed: 7);
        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01085"],
            ceilingMaterial: materials["01138"]);
        var lights = MazeGeometryBuilder.BuildLights(maze);

        var camera = new Camera
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 2f,
            Aspect = (float)width / height,
            ImgPlaneZ = 1f
        };

        return new JobSystem(
            width,
            height,
            scene,
            camera,
            width * 4,
            lights,
            new RenderOptions(TileSize: 8, SppPerJob: 1, MaxSampleCount: 64),
            new SamplingOptions(MotionSampleCap: 8, SubPixelJitter: false),
            denoise ?? new DenoiseOptions(
                FilterRadius: 0, EdgeAwareFilter: false, CheckerboardMotion: false,
                TemporalBlendAlpha: 0.5f, EnableTaa: true, SampleClamp: 2f),
            new DebugOptions());
    }

    // ── Value-type guarantees ─────────────────────────────────────────────

    [TestMethod]
    public void Tile_IsValueType()
    {
        Assert.IsTrue(
            typeof(Tile).IsValueType,
            "Tile must be a value type (struct) to avoid per-tile heap allocations in the channel ring-buffer.");
    }

    [TestMethod]
    public void FrameDiagnostics_IsValueType()
    {
        Assert.IsTrue(
            typeof(FrameDiagnostics).IsValueType,
            "FrameDiagnostics must be a value type for zero-allocation passing between resolve and callers.");
    }

    [TestMethod]
    public void Matrix3x3_IsValueType()
    {
        Assert.IsTrue(
            typeof(Matrix3x3).IsValueType,
            "Matrix3x3 must be a value type (struct) to avoid heap allocation on every ToSRGB call.");
    }

    // ── Frame diagnostics logging ────────────────────────────────────────

    [TestMethod]
    public void FrameDiagnostics_IsPopulatedAfterFirstResolve()
    {
        var js = CreateJobSystem();
        js.ResolveDisplayBufferWithTaa();

        var diag = js.LastFrameDiagnostics;
        Assert.AreEqual(1, diag.FrameIndex, "FrameIndex should be 1 after the first resolve.");
        Assert.IsTrue(diag.ResolveTaaMs >= 0, "ResolveTaaMs should be non-negative.");
    }

    [TestMethod]
    public void FrameDiagnostics_FrameIndexIncrementsEachResolve()
    {
        var js = CreateJobSystem();

        js.ResolveDisplayBufferWithTaa();
        int first = js.LastFrameDiagnostics.FrameIndex;

        js.ResolveDisplayBufferWithTaa();
        int second = js.LastFrameDiagnostics.FrameIndex;

        Assert.AreEqual(first + 1, second, "FrameIndex should increment by 1 on each resolve.");
    }

    [TestMethod]
    public void FrameDiagnostics_ResolveTaaMs_IsPositive()
    {
        var js = CreateJobSystem();
        js.ResolveDisplayBufferWithTaa();

        Assert.IsTrue(
            js.LastFrameDiagnostics.ResolveTaaMs >= 0,
            "ResolveTaaMs must be non-negative.");
    }

    [TestMethod]
    public void FrameDiagnostics_TaaDisabled_ZeroRejectionPercent()
    {
        var js = CreateJobSystem(denoise: new DenoiseOptions(
            FilterRadius: 0, EdgeAwareFilter: false, CheckerboardMotion: false,
            TemporalBlendAlpha: 0f, EnableTaa: false, SampleClamp: 0f));

        js.ResolveDisplayBufferWithTaa();

        Assert.AreEqual(0.0, js.LastFrameDiagnostics.RejectedHistoryPercent,
            "No history rejection should occur when TAA is disabled.");
    }

    // ── Thread-safe counters ─────────────────────────────────────────────

    [TestMethod]
    public void TotalRays_StartsAtZero()
    {
        var js = CreateJobSystem();
        Assert.AreEqual(0L, js.TotalRays, "TotalRays should be zero before any tiles are processed.");
    }

    [TestMethod]
    public void TotalTileCompletions_StartsAtZero()
    {
        var js = CreateJobSystem();
        Assert.AreEqual(0L, js.TotalTileCompletions, "TotalTileCompletions should be zero before any tiles are processed.");
    }

    // ── Allocation + throughput snapshot ─────────────────────────────────

    [TestMethod]
    public async Task AllocationBaseline_Phase5_Snapshot()
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(4, 4, seed: 42);

        materials.TryGetMaterial("01085", out var mortarMat);
        materials.TryGetMaterial("01138", out var ceilingGridMat);

        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01138"],
            ceilingMaterial: materials["01085"],
            mortarMaterial: mortarMat,
            ceilingGridMaterial: ceilingGridMat,
            goalMaterial: null);
        var lights = MazeGeometryBuilder.BuildLights(maze);

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        var camera = new Camera
        {
            Position = new Vector3(cs * 0.5f, wh * 0.5f, cs * 0.5f),
            Rotation = CameraController.HeadingToQuaternion(Direction.South),
            Fov = MathF.PI / 3f,
            Aspect = 64f / 48f,
            ImgPlaneZ = 1f
        };

        var allocBefore = GC.GetTotalAllocatedBytes(true);
        var sw = Stopwatch.StartNew();

        double rps = await PerformanceCalibrator.MeasureRaysPerSecondAsync(
            scene, camera, lights, TimeSpan.FromMilliseconds(250));

        sw.Stop();
        long allocDelta = GC.GetTotalAllocatedBytes(true) - allocBefore;
        long workingSet = Process.GetCurrentProcess().WorkingSet64;

        TestContext?.WriteLine(
            $"Phase5 baseline | elapsed_ms={sw.Elapsed.TotalMilliseconds:F2} | " +
            $"rays_per_sec={rps:F0} | allocated_bytes={allocDelta} | " +
            $"working_set_bytes={workingSet}");

        Assert.IsTrue(rps > 0, "Expected positive rays/sec.");
        Assert.IsTrue(allocDelta >= 0, "Allocated byte delta should be non-negative.");
    }

    public TestContext? TestContext { get; set; }
}
