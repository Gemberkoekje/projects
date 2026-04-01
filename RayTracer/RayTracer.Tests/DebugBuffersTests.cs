using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class DebugBuffersTests
{
    private static JobSystem CreateJobSystem(int width = 32, int height = 24)
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(2, 2, seed: 7);
        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01085"],
            ceilingMaterial: materials["01138"]);

        var camera = new Camera
        {
            Position = new Vector3(1f, 1.5f, 1f),
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 3f,
            Aspect = (float)width / height,
            ImgPlaneZ = 1f
        };

        return new JobSystem(width, height, 8, scene, camera, width * 4);
    }

    [TestMethod]
    public void NewDebugModes_HaveLegends()
    {
        var js = CreateJobSystem();

        var albedo = js.GetDebugLegend(DebugViewMode.Albedo);
        var normal = js.GetDebugLegend(DebugViewMode.Normal);
        var depth = js.GetDebugLegend(DebugViewMode.Depth);

        Assert.IsTrue(albedo.Contains("Albedo"));
        Assert.IsTrue(normal.Contains("Normal"));
        Assert.IsTrue(depth.Contains("Depth"));
    }

    [TestMethod]
    public void RenderDebugModes_WritesOpaqueBuffer()
    {
        var js = CreateJobSystem();
        var buffer = new byte[js.Width * js.Height * 4];

        js.RenderDebugModeToBuffer(DebugViewMode.Albedo, buffer, js.Width * 4);
        for (int i = 3; i < buffer.Length; i += 4)
            Assert.AreEqual(255, buffer[i]);

        js.RenderDebugModeToBuffer(DebugViewMode.Normal, buffer, js.Width * 4);
        for (int i = 3; i < buffer.Length; i += 4)
            Assert.AreEqual(255, buffer[i]);
    }
}
