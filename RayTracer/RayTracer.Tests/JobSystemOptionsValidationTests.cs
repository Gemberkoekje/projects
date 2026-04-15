using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class JobSystemOptionsValidationTests
{
    private static (Tracable[] scene, Camera camera, Light[] lights) CreateState(int width = 16, int height = 12)
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(2, 2, seed: 123);
        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01085"],
            ceilingMaterial: materials["01138"]);
        var lights = MazeGeometryBuilder.BuildLights(maze);

        var camera = new Camera
        {
            Position = new Vector3(1f, 1.5f, 1f),
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 3f,
            Aspect = (float)width / height,
            ImgPlaneZ = 1f
        };

        return (scene, camera, lights);
    }

    private static void AssertThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
            Assert.Fail($"Expected exception of type {typeof(T).Name}.");
        }
        catch (T)
        {
        }
    }

    [TestMethod]
    public void Constructor_InvalidWidth_Throws()
    {
        var (scene, camera, lights) = CreateState();

        AssertThrows<ArgumentOutOfRangeException>(() =>
            _ = new JobSystem(0, 12, scene, camera, 16 * 4, lights));
    }

    [TestMethod]
    public void Constructor_InvalidStride_Throws()
    {
        var (scene, camera, lights) = CreateState();

        AssertThrows<ArgumentOutOfRangeException>(() =>
            _ = new JobSystem(16, 12, scene, camera, 16 * 4 - 1, lights));
    }

    [TestMethod]
    public void Constructor_InvalidOptions_Throws()
    {
        var (scene, camera, lights) = CreateState();

        AssertThrows<ArgumentOutOfRangeException>(() =>
            _ = new JobSystem(
                16,
                12,
                scene,
                camera,
                16 * 4,
                lights,
                renderOptions: new RenderOptions(TileSize: 0),
                samplingOptions: new SamplingOptions(),
                denoiseOptions: new DenoiseOptions(),
                debugOptions: new DebugOptions()));

        AssertThrows<ArgumentOutOfRangeException>(() =>
            _ = new JobSystem(
                16,
                12,
                scene,
                camera,
                16 * 4,
                lights,
                renderOptions: new RenderOptions(),
                samplingOptions: new SamplingOptions(),
                denoiseOptions: new DenoiseOptions(TemporalBlendAlpha: 1.1f),
                debugOptions: new DebugOptions()));
    }
}
