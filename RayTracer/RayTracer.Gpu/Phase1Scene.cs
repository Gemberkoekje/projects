using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Builds the maze scene, camera, and GPU resources for Phase 1, mirroring the
/// CPU app's setup (<c>MiniRay.Program</c>) so the GPU render can be compared
/// against the CPU reference.
/// </summary>
internal sealed class Phase1Scene
{
    public required Tracable[] Tracables { get; init; }
    public required PackedScene Packed { get; init; }
    public required SpectralResources Spectral { get; init; }
    public required Camera Camera { get; init; }
    public required WavelengthLookup Wavelengths { get; init; }

    public static Phase1Scene Build(int width, int height, int mazeSeed = 12345, int mazeSize = 16)
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(mazeSize, mazeSize, seed: mazeSeed);

        materials.TryGetMaterial("01138", out MaterialData goal);
        materials.TryGetMaterial("01085", out MaterialData mortar);
        materials.TryGetMaterial("01138", out MaterialData grid);

        Tracable[] tracables = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01138"],
            ceilingMaterial: materials["01085"],
            mortarMaterial: mortar,
            ceilingGridMaterial: grid,
            goalMaterial: goal);

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        float eyeHeight = wh * 0.5f;

        var camera = new Camera
        {
            Position = new Vector3(cs * 0.5f, eyeHeight, cs * 0.5f),
            Rotation = CameraController.HeadingToQuaternion(Direction.South),
            Fov = MathF.PI / 3f,
            Aspect = (float)width / height,
            ImgPlaneZ = 1f,
        };

        var wavelengths = new WavelengthLookup();
        PackedScene packed = GpuScenePacker.Pack(tracables);
        SpectralResources spectral = SpectralResourceBaker.Bake(wavelengths, packed.Materials);

        return new Phase1Scene
        {
            Tracables = tracables,
            Packed = packed,
            Spectral = spectral,
            Camera = camera,
            Wavelengths = wavelengths,
        };
    }
}
