using BenchmarkDotNet.Attributes;
using RayTracer;
using System.Numerics;
using Microsoft.VSDiagnostics;

namespace Benchmarks;
[CPUUsageDiagnoser]
public class BVHBenchmarks
{
    public int Iterations = 100_000;
    private BVH _bvh;
    private Ray[] _rays;

    [Params(0, 1, 4, 8)]
    public int MaxBounces { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(16, 16, seed: 42);
        var scene = MazeGeometryBuilder.Build(maze, wallMaterial: materials["00115"], floorMaterial: materials["01085"], ceilingMaterial: materials["01138"]);
        _bvh = new BVH(scene);
        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        Vector3 origin = new(cs * 0.5f, wh * 0.5f, cs * 0.5f);
        var rng = new Random(42);
        _rays = new Ray[Iterations];
        for (int i = 0; i < Iterations; i++)
        {
            Vector3 dir = Vector3.Normalize(new Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1)));
            _rays[i] = new Ray
            {
                Origin = origin,
                Direction = dir,
                Wavelength = 550,
                Intensity = 1f
            };
        }
    }

    [Benchmark]
    public int FindClosest()
    {
        var result = 0;
        for (int i = 0; i < Iterations; i++)
        {
            var ray = _rays[i];
            var (_, _, _, _, hit, _) = _bvh.FindClosest(ray);
            result += hit ? 1 : 0;
        }

        return result;
    }

    [Benchmark]
    public int FindClosestWithBounces()
    {
        // Deterministic per-ray seed so results are reproducible across runs.
        var result = 0;
        for (int i = 0; i < Iterations; i++)
        {
            var ray = _rays[i];
            uint seed = (uint)i;
            int bounces = 0;

            while (bounces <= MaxBounces)
            {
                var (reflectance, hitPoint, hitNormal, roughness, hit, _) = _bvh.FindClosest(ray);
                if (!hit)
                    break;

                result++;
                bounces++;
                if (bounces > MaxBounces)
                    break;

                // Attenuate intensity by surface reflectance.
                ray.Intensity *= reflectance;
                if (ray.Intensity < 1e-4f)
                    break;

                // Compute bounce direction. Roughness controls how much
                // the reflected ray scatters: 0 = perfect mirror,
                // 1 = fully diffuse (cosine-weighted hemisphere).
                Vector3 reflected = Vector3.Reflect(ray.Direction, hitNormal);

                // Generate a cosine-weighted hemisphere sample for diffuse scatter.
                Vector3 diffuse = CosineHemisphere(hitNormal, ref seed);

                // Lerp between mirror reflection and diffuse based on roughness.
                Vector3 dir = Vector3.Normalize(
                    Vector3.Lerp(reflected, diffuse, roughness));

                // Offset origin slightly along normal to avoid self-intersection.
                ray.Origin = hitPoint + hitNormal * 1e-3f;
                ray.Direction = dir;
            }
        }

        return result;
    }

    /// <summary>
    /// Cosine-weighted hemisphere sample around the given normal using
    /// a fast xorshift PRNG. Produces diffuse-like scatter directions.
    /// </summary>
    private static Vector3 CosineHemisphere(Vector3 normal, ref uint seed)
    {
        float u1 = NextFloat(ref seed);
        float u2 = NextFloat(ref seed);

        // Uniform point on unit disk, projected to hemisphere.
        float r = MathF.Sqrt(u1);
        float phi = 2f * MathF.PI * u2;
        float x = r * MathF.Cos(phi);
        float z = r * MathF.Sin(phi);
        float y = MathF.Sqrt(MathF.Max(0f, 1f - u1));

        // Build tangent-space basis from normal.
        Vector3 tangent = MathF.Abs(normal.X) > 0.9f
            ? Vector3.Normalize(Vector3.Cross(new Vector3(0, 1, 0), normal))
            : Vector3.Normalize(Vector3.Cross(new Vector3(1, 0, 0), normal));
        Vector3 bitangent = Vector3.Cross(normal, tangent);

        return Vector3.Normalize(tangent * x + normal * y + bitangent * z);
    }

    private static float NextFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0x00FFFFFFu) * (1f / 0x01000000u);
    }
}