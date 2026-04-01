using BenchmarkDotNet.Attributes;
using RayTracer;
using System.Numerics;

namespace Benchmarks;

public class DebugResolveBenchmark
{
    private JobSystem _jobSystem = null!;
    private byte[] _debugBuffer = null!;
    private int _stride;

    [Params(DebugViewMode.SampleCount, DebugViewMode.Variance, DebugViewMode.Depth, DebugViewMode.DirectLighting)]
    public DebugViewMode Mode { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        const int width = 800;
        const int height = 600;
        _stride = width * 4;

        var materials = new MaterialsLookup();
        var maze = new Maze(6, 6, seed: 21);
        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01085"],
            ceilingMaterial: materials["01138"]);
        var lights = MazeGeometryBuilder.BuildLights(maze);

        var camera = new Camera
        {
            Position = new Vector3(1.2f, 1.2f, 1.2f),
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 3f,
            Aspect = (float)width / height,
            ImgPlaneZ = 1f
        };

        _jobSystem = new JobSystem(width, height, 32, scene, camera, _stride, lights)
        {
            EnableTaa = true,
            TemporalBlendAlpha = 0.05f
        };
        _debugBuffer = new byte[_stride * height];

        // Fill buffers with representative, deterministic content.
        for (int i = 0; i < _jobSystem.AccumXYZ.Length; i++)
        {
            float t = (i % width) / (float)width;
            _jobSystem.AccumXYZ[i] = new Vector3(0.2f + t, 0.15f + t * 0.5f, 0.1f + t * 0.25f);
            _jobSystem.SampleCount[i] = (uint)(1 + (i % 128));
            _jobSystem.LastHit[i] = true;
        }

        _jobSystem.ResolveDisplayBufferWithTaa();
    }

    [Benchmark(Baseline = true)]
    public void ResolveBeauty()
    {
        _jobSystem.ResolveDisplayBufferWithTaa();
    }

    [Benchmark]
    public void ResolveDebugMode()
    {
        _jobSystem.RenderDebugModeToBuffer(Mode, _debugBuffer, _stride);
    }
}
