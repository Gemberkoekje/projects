using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Builds the maze scene, lights, camera, and GPU resources for Phase 2. Mirrors
/// <see cref="Phase1Scene"/> but also builds the maze lights
/// (<see cref="MazeGeometryBuilder.BuildLights"/>) with a <b>fixed</b> seed so
/// the GPU render is deterministic and can be cross-checked against the CPU
/// reference. The CPU app seeds lights with <c>Environment.TickCount</c>; here a
/// constant keeps the headless self-test reproducible.
/// </summary>
internal sealed class Phase2Scene
{
    public required Tracable[] Tracables { get; init; }
    public required PackedScene Packed { get; init; }
    public required SpectralResources Spectral { get; init; }
    public required Light[] Lights { get; init; }
    public required PackedLights PackedLights { get; init; }
    public required Camera Camera { get; init; }
    public required WavelengthLookup Wavelengths { get; init; }

    public static Phase2Scene Build(int width, int height, int mazeSeed = 12345, int mazeSize = 16, int lightSeed = 777)
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

        Light[] lights = MazeGeometryBuilder.BuildLights(
            maze,
            lightSpawnChance: 0.4f,
            biomeSize: 4,
            seed: lightSeed);

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
        PackedLights packedLights = LightPacker.Pack(lights);

        return new Phase2Scene
        {
            Tracables = tracables,
            Packed = packed,
            Spectral = spectral,
            Lights = lights,
            PackedLights = packedLights,
            Camera = camera,
            Wavelengths = wavelengths,
        };
    }
}
