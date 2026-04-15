using System.Diagnostics;
using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class Phase0SmokeTests
{
    private static (Tracable[] scene, Light[] lights, Camera camera) CreateCoreStartupState(int width = 32, int height = 24)
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
        float eyeHeight = wh * 0.5f;
        var camera = new Camera
        {
            Position = new Vector3(cs * 0.5f, eyeHeight, cs * 0.5f),
            Rotation = CameraController.HeadingToQuaternion(Direction.South),
            Fov = MathF.PI / 3f,
            Aspect = (float)width / height,
            ImgPlaneZ = 1f
        };

        return (scene, lights, camera);
    }

    [TestMethod]
    public void StartupSmoke_CanBuildSceneAndCreateJobSystem()
    {
        var (scene, lights, camera) = CreateCoreStartupState();

        var js = new JobSystem(32, 24, 8, scene, camera, 32 * 4, lights);

        Assert.AreEqual(32, js.Width);
        Assert.AreEqual(24, js.Height);
        Assert.AreEqual(32 * 24 * 4, js.DisplayBuffer.Length);
        Assert.IsTrue(scene.Length > 0);
        Assert.IsTrue(lights.Length > 0);
    }

    [TestMethod]
    public void MinimalRenderPathSmoke_ResolveDisplayBufferWithTaa_WritesOpaqueOutput()
    {
        var (scene, lights, camera) = CreateCoreStartupState();
        var js = new JobSystem(32, 24, 8, scene, camera, 32 * 4, lights);

        js.ResolveDisplayBufferWithTaa();

        for (int i = 3; i < js.DisplayBuffer.Length; i += 4)
            Assert.AreEqual(255, js.DisplayBuffer[i]);
    }

    [TestMethod]
    public async Task BaselineSmoke_CapturesRaysPerSecondAndAllocationSnapshot()
    {
        var (scene, lights, camera) = CreateCoreStartupState(64, 48);

        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var sw = Stopwatch.StartNew();

        double rps = await PerformanceCalibrator.MeasureRaysPerSecondAsync(
            scene,
            camera,
            lights,
            TimeSpan.FromMilliseconds(250));

        sw.Stop();
        var allocatedAfter = GC.GetTotalAllocatedBytes(true);
        long allocatedDelta = allocatedAfter - allocatedBefore;
        long workingSet = Process.GetCurrentProcess().WorkingSet64;

        TestContext?.WriteLine($"Phase0 baseline | elapsed_ms={sw.Elapsed.TotalMilliseconds:F2} | rays_per_sec={rps:F0} | allocated_bytes={allocatedDelta} | working_set_bytes={workingSet}");

        Assert.IsTrue(rps > 0, "Expected positive rays/sec baseline.");
        Assert.IsTrue(allocatedDelta >= 0, "Allocated byte delta should be non-negative.");
    }

    public TestContext? TestContext { get; set; }
}
