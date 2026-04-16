using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class PerformanceCalibratorTests
{
    [TestMethod]
    public async Task MeasureRaysPerSecondAsync_CompletesAndReportsProgress()
    {
        var (scene, lights, camera) = CreateCoreStartupState();
        var progressValues = new List<double>();
        var progress = new Progress<double>(v => progressValues.Add(v));

        double rps = await PerformanceCalibrator.MeasureRaysPerSecondAsync(
            scene,
            camera,
            lights,
            TimeSpan.FromMilliseconds(100),
            benchPreset: RenderPreset.Low,
            progress: progress);

        Assert.IsTrue(rps >= 0d);
        Assert.IsTrue(progressValues.Count > 0, "Expected at least one progress callback.");
        Assert.AreEqual(1.0d, progressValues[^1], 1e-6d, "Final progress callback should be 1.0.");
    }

    [TestMethod]
    public async Task MeasureRaysPerSecondAsync_PreCanceledToken_ReturnsWithoutThrowing()
    {
        var (scene, lights, camera) = CreateCoreStartupState();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        double rps = await PerformanceCalibrator.MeasureRaysPerSecondAsync(
            scene,
            camera,
            lights,
            TimeSpan.FromMilliseconds(500),
            benchPreset: RenderPreset.Low,
            ct: cts.Token);

        Assert.IsTrue(rps >= 0d);
    }

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
}
