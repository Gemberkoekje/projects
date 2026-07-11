using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;
using RayTracer;
using System.Numerics;

namespace Benchmarks;

[CPUUsageDiagnoser]
public class SmokeBenchmarks
{
    private const int Iterations = 50_000;

    private BVH _bvh = null!;
    private Ray[] _rays = null!;
    private Vector3[] _hitPoints = null!;
    private VolumetricOptions _volOptsMedium;
    private VolumetricOptions _volOptsHigh;
    private VolumetricOptions _volOptsUltra;

    [GlobalSetup]
    public void Setup()
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(16, 16, seed: 42);
        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01085"],
            ceilingMaterial: materials["01138"]);
        _bvh = new BVH(scene);

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        Vector3 origin = new(cs * 0.5f, wh * 0.5f, cs * 0.5f);
        var rng = new Random(42);

        _rays = new Ray[Iterations];
        _hitPoints = new Vector3[Iterations];

        for (int i = 0; i < Iterations; i++)
        {
            Vector3 dir = Vector3.Normalize(new Vector3(
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1)));

            _rays[i] = new Ray
            {
                Origin = origin,
                Direction = dir,
                Wavelength = 550,
                Intensity = 1f
            };

            float dist = 2f + (float)rng.NextDouble() * 10f;
            _hitPoints[i] = origin + dir * dist;
        }

        _volOptsMedium = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.Biome);
        _volOptsHigh = VolumetricOptions.FromQuality(VolumetricQuality.High, SmokeMode.Biome);
        _volOptsUltra = VolumetricOptions.FromQuality(VolumetricQuality.Ultra, SmokeMode.Biome);
    }

    [Benchmark(Baseline = true)]
    public float VolumetricSegment_Medium()
    {
        float sum = 0f;
        for (int i = 0; i < Iterations; i++)
        {
            var sample = JobSystem.IntegrateVolumetricSegment(
                _rays[i].Origin,
                _hitPoints[i],
                _rays[i].Direction,
                _volOptsMedium);
            sum += sample.Transmittance;
        }
        return sum;
    }

    [Benchmark]
    public float VolumetricSegment_High()
    {
        float sum = 0f;
        for (int i = 0; i < Iterations; i++)
        {
            var sample = JobSystem.IntegrateVolumetricSegment(
                _rays[i].Origin,
                _hitPoints[i],
                _rays[i].Direction,
                _volOptsHigh);
            sum += sample.Transmittance;
        }
        return sum;
    }

    [Benchmark]
    public int BVH_FindClosest_WithSmoke()
    {
        int hits = 0;
        for (int i = 0; i < Iterations; i++)
        {
            var (_, _, _, _, hit, _, _, _) = _bvh.FindClosest(_rays[i]);
            if (hit) hits++;
        }
        return hits;
    }

    /// <summary>
    /// High preset: shadow ray every 4th step (ShadowStepInterval = 4).
    /// Exercises the new configurable shadow-sampling path with a BVH.
    /// </summary>
    [Benchmark]
    public float VolumetricSegment_High_ShadowInterval4()
    {
        float sum = 0f;
        for (int i = 0; i < Iterations; i++)
        {
            var sample = JobSystem.IntegrateVolumetricSegment(
                _rays[i].Origin,
                _hitPoints[i],
                _rays[i].Direction,
                _volOptsHigh);
            sum += sample.Transmittance;
        }
        return sum;
    }

    /// <summary>
    /// Ultra preset: shadow ray every step (ShadowStepInterval = 1).
    /// Establishes the upper-bound cost for full per-step shadow tracing.
    /// </summary>
    [Benchmark]
    public float VolumetricSegment_Ultra_ShadowInterval1()
    {
        float sum = 0f;
        for (int i = 0; i < Iterations; i++)
        {
            var sample = JobSystem.IntegrateVolumetricSegment(
                _rays[i].Origin,
                _hitPoints[i],
                _rays[i].Direction,
                _volOptsUltra);
            sum += sample.Transmittance;
        }
        return sum;
    }
}
