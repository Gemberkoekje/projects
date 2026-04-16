using System.Numerics;
using System.Reflection;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class DiffuseIrradianceCacheTests
{
    [TestMethod]
    public void AccumulateAndLookup_ReturnsAveragedIrradiance()
    {
        var cache = new DiffuseIrradianceCache(0.25f, minSamples: 2);
        Vector3 pos = new(1f, 2f, 3f);
        Vector3 normal = Vector3.UnitY;

        cache.Accumulate(pos, normal, new Vector3(1f, 0f, 0f));
        Assert.IsFalse(cache.TryLookup(pos, normal, out _));

        cache.Accumulate(pos, normal, new Vector3(3f, 0f, 0f));
        Assert.IsTrue(cache.TryLookup(pos, normal, out Vector3 value));
        Assert.AreEqual(2f, value.X, 1e-5f);
    }

    [TestMethod]
    public void EvictStale_RemovesOldEntries()
    {
        var cache = new DiffuseIrradianceCache(0.25f, minSamples: 1);
        Vector3 pos = new(0.1f, 0.2f, 0.3f);
        Vector3 normal = Vector3.UnitY;

        cache.Accumulate(pos, normal, new Vector3(0.5f));
        Assert.IsTrue(cache.TryLookup(pos, normal, out _));

        cache.EvictStale(currentFrame: 1, maxAge: 10);
        Assert.AreEqual(1, cache.EntryCount);

        cache.EvictStale(currentFrame: 20, maxAge: 5);
        Assert.AreEqual(0, cache.EntryCount);
        Assert.IsFalse(cache.TryLookup(pos, normal, out _));
    }

    [TestMethod]
    public void Clear_RemovesAllEntriesAndResetsStats()
    {
        var cache = new DiffuseIrradianceCache(0.25f, minSamples: 1);
        cache.Accumulate(new Vector3(1f), Vector3.UnitY, new Vector3(0.2f));
        Assert.IsTrue(cache.TryLookup(new Vector3(1f), Vector3.UnitY, out _));

        cache.Clear();

        Assert.AreEqual(0, cache.EntryCount);
        Assert.AreEqual(0d, cache.HitRate, 1e-9);
        Assert.IsFalse(cache.TryLookup(new Vector3(1f), Vector3.UnitY, out _));
    }

    [TestMethod]
    public void ThreadSafetySmokeTest_ConcurrentAccumulateAndLookup_DoesNotThrow()
    {
        var cache = new DiffuseIrradianceCache(0.25f, minSamples: 1);
        Vector3 n = Vector3.UnitY;

        Parallel.For(0, 2000, i =>
        {
            Vector3 p = new(i % 16, (i / 16) % 16, (i / 256) % 16);
            cache.Accumulate(p, n, new Vector3(0.1f, 0.2f, 0.3f));
            _ = cache.TryLookup(p, n, out _);
        });

        Assert.IsTrue(cache.EntryCount > 0);
    }

    [TestMethod]
    public void JobSystemSoftReset_PreservesCache_WhileHardResetClears()
    {
        var (scene, camera, lights) = CreateState();
        var job = new JobSystem(
            16,
            12,
            scene,
            camera,
            16 * 4,
            lights,
            denoiseOptions: new DenoiseOptions(EnableDiffuseCache: true, DiffuseCacheMinSamples: 1));

        FieldInfo? field = typeof(JobSystem).GetField("_irradianceCache", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        var cache = (DiffuseIrradianceCache)field.GetValue(job)!;

        Vector3 pos = new(1f, 1f, 1f);
        Vector3 normal = Vector3.UnitY;
        cache.Accumulate(pos, normal, new Vector3(0.2f, 0.3f, 0.4f));
        Assert.IsTrue(cache.TryLookup(pos, normal, out _));

        job.SoftResetAccumulation();
        Assert.IsTrue(cache.TryLookup(pos, normal, out _));

        job.ResetAccumulation();
        Assert.IsFalse(cache.TryLookup(pos, normal, out _));
    }

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
}
