using System.Diagnostics;
using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class Phase4TestingExpansionTests
{
    private static JobSystem CreateJobSystem(
        int width,
        int height,
        DenoiseOptions? denoise = null)
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(2, 2, seed: 7);
        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01085"],
            ceilingMaterial: materials["01138"]);

        var lights = MazeGeometryBuilder.BuildLights(maze);

        var camera = new Camera
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 2f,
            Aspect = (float)width / height,
            ImgPlaneZ = 1f
        };

        return new JobSystem(
            width,
            height,
            scene,
            camera,
            width * 4,
            lights,
            new RenderOptions(TileSize: 8, SppPerJob: 1, MaxSampleCount: 500),
            new SamplingOptions(MotionSampleCap: 20, SubPixelJitter: false),
            denoise ?? new DenoiseOptions(FilterRadius: 0, EdgeAwareFilter: false, CheckerboardMotion: false, TemporalBlendAlpha: 0f, EnableTaa: false, SampleClamp: 0f),
            new DebugOptions());
    }

    [TestMethod]
    public void SpatialFilter_BoxFilter_DeterministicNeighborhoodAverage()
    {
        var js = CreateJobSystem(3, 3, new DenoiseOptions(FilterRadius: 1, EdgeAwareFilter: false, CheckerboardMotion: false, TemporalBlendAlpha: 0f, EnableTaa: false, SampleClamp: 0f));
        js.IsMoving = true;

        Array.Fill(js.AccumXYZ, Vector3.Zero);
        js.AccumXYZ[4] = new Vector3(9f, 9f, 9f);

        var info = js.GetPixelDebugInfo(1, 1);

        Assert.AreEqual(1f, info.FilteredXYZ.X, 0.0001f);
        Assert.AreEqual(1f, info.FilteredXYZ.Y, 0.0001f);
        Assert.AreEqual(1f, info.FilteredXYZ.Z, 0.0001f);
    }

    [TestMethod]
    public void SpatialFilter_EdgeAware_PreservesCenterAgainstBrightNeighbors()
    {
        var edgeAware = CreateJobSystem(3, 3, new DenoiseOptions(FilterRadius: 1, EdgeAwareFilter: true, CheckerboardMotion: false, TemporalBlendAlpha: 0f, EnableTaa: false, SampleClamp: 0f));
        var box = CreateJobSystem(3, 3, new DenoiseOptions(FilterRadius: 1, EdgeAwareFilter: false, CheckerboardMotion: false, TemporalBlendAlpha: 0f, EnableTaa: false, SampleClamp: 0f));

        edgeAware.IsMoving = true;
        box.IsMoving = true;

        Array.Fill(edgeAware.AccumXYZ, new Vector3(10f, 10f, 10f));
        Array.Fill(box.AccumXYZ, new Vector3(10f, 10f, 10f));

        edgeAware.AccumXYZ[4] = Vector3.One;
        box.AccumXYZ[4] = Vector3.One;

        var edgeAwareCenter = edgeAware.GetPixelDebugInfo(1, 1).FilteredXYZ;
        var boxCenter = box.GetPixelDebugInfo(1, 1).FilteredXYZ;

        Assert.IsTrue(edgeAwareCenter.Y < 2f, $"Edge-aware center Y={edgeAwareCenter.Y} should stay near 1.");
        Assert.IsTrue(boxCenter.Y > 8f, $"Box-filter center Y={boxCenter.Y} should be heavily influenced by neighbors.");
    }

    [TestMethod]
    public void TaaResolve_RejectsHistory_WhenReprojectionErrorIsHigh()
    {
        var js = CreateJobSystem(1, 1, new DenoiseOptions(FilterRadius: 0, EdgeAwareFilter: false, CheckerboardMotion: false, TemporalBlendAlpha: 0.5f, EnableTaa: true, SampleClamp: 0f));

        js.AccumXYZ[0] = new Vector3(1f, 1f, 1f);
        js.LastHit[0] = true;
        js.PerPixel.HitPointWorld[0] = new Vector3(0f, 0f, 2f);
        js.Debug.NormalWorld[0] = new Vector3(0f, 0f, -1f);

        js.ResolveDisplayBufferWithTaa();

        js.AccumXYZ[0] = Vector3.Zero;
        js.LastHit[0] = true;
        js.PerPixel.HitPointWorld[0] = new Vector3(0f, 0f, 4f);
        js.Debug.NormalWorld[0] = new Vector3(0f, 0f, -1f);

        js.ResolveDisplayBufferWithTaa();

        var resolved = js.History.HistoryXYZ[0];
        Assert.AreEqual(0f, resolved.X, 0.0001f);
        Assert.AreEqual(0f, resolved.Y, 0.0001f);
        Assert.AreEqual(0f, resolved.Z, 0.0001f);
        Assert.AreEqual(1, js.Debug.HistoryRejected[0]);
        Assert.AreEqual(0.0, js.AverageHistoryWeight, 0.0001);
        Assert.AreEqual(100.0, js.RejectedHistoryPercent, 0.0001);
    }

    [TestMethod]
    public void TaaResolve_FirstFrameWithoutHistory_DoesNotBlend()
    {
        var js = CreateJobSystem(2, 1, new DenoiseOptions(FilterRadius: 0, EdgeAwareFilter: false, CheckerboardMotion: false, TemporalBlendAlpha: 0.5f, EnableTaa: true, SampleClamp: 0f));

        js.AccumXYZ[0] = new Vector3(1f, 1f, 1f);
        js.AccumXYZ[1] = new Vector3(1f, 1f, 1f);
        js.LastHit[0] = true;
        js.LastHit[1] = true;
        js.PerPixel.HitPointWorld[0] = new Vector3(-1f, 0f, 2f);
        js.PerPixel.HitPointWorld[1] = new Vector3(1f, 0f, 2f);

        js.ResolveDisplayBufferWithTaa();

        Assert.AreEqual(0.0, js.AverageHistoryWeight, 0.0001);
        Assert.AreEqual(0.0, js.RejectedHistoryPercent, 0.0001);
        Assert.AreEqual(1, js.FrameIndex);
    }

    [TestMethod]
    public void Property_ToSrgbIsLinear_ForRandomInputPairs()
    {
        var rng = new Random(12345);

        for (int i = 0; i < 200; i++)
        {
            var a = new Vector3(
                (float)(rng.NextDouble() * 4.0 - 2.0),
                (float)(rng.NextDouble() * 4.0 - 2.0),
                (float)(rng.NextDouble() * 4.0 - 2.0));

            var b = new Vector3(
                (float)(rng.NextDouble() * 4.0 - 2.0),
                (float)(rng.NextDouble() * 4.0 - 2.0),
                (float)(rng.NextDouble() * 4.0 - 2.0));

            var left = JobSystem.ToSRGB(a + b);
            var right = JobSystem.ToSRGB(a) + JobSystem.ToSRGB(b);

            Assert.AreEqual(left.X, right.X, 0.0001f);
            Assert.AreEqual(left.Y, right.Y, 0.0001f);
            Assert.AreEqual(left.Z, right.Z, 0.0001f);
        }
    }

    [TestMethod]
    public void Property_RunningAverageStaysWithinObservedBounds_Randomized()
    {
        var rng = new Random(999);
        var mean = Vector3.Zero;
        uint count = 0;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        for (int i = 0; i < 1000; i++)
        {
            var sample = new Vector3(
                (float)rng.NextDouble() * 2f,
                (float)rng.NextDouble() * 2f,
                (float)rng.NextDouble() * 2f);

            min = Vector3.Min(min, sample);
            max = Vector3.Max(max, sample);

            count++;
            mean += (sample - mean) / count;

            Assert.IsTrue(mean.X >= min.X - 1e-5f && mean.X <= max.X + 1e-5f);
            Assert.IsTrue(mean.Y >= min.Y - 1e-5f && mean.Y <= max.Y + 1e-5f);
            Assert.IsTrue(mean.Z >= min.Z - 1e-5f && mean.Z <= max.Z + 1e-5f);
        }
    }

    [TestMethod]
    public void Integration_ResolveDisplayBuffer_ProducesStableSnapshotChecksum()
    {
        var js = CreateJobSystem(2, 2);

        js.AccumXYZ[0] = Vector3.Zero;
        js.AccumXYZ[1] = new Vector3(0.95047f, 1.0f, 1.08883f);
        js.AccumXYZ[2] = new Vector3(0.475235f, 0.5f, 0.544415f);
        js.AccumXYZ[3] = new Vector3(0.2376175f, 0.25f, 0.2722075f);

        js.ResolveDisplayBufferWithTaa();

        int checksum = 0;
        for (int i = 0; i < js.DisplayBuffer.Length; i++)
            checksum += js.DisplayBuffer[i];

        Assert.AreEqual(2753, checksum, "Regression snapshot checksum changed.");
    }

    [TestMethod]
    [TestCategory("Performance")]
    public void Performance_ToSrgbTransform_EmitsThroughputSnapshot()
    {
        var sw = Stopwatch.StartNew();
        var acc = Vector3.Zero;

        for (int i = 0; i < 2_000_000; i++)
        {
            float t = i * 0.000001f;
            acc += JobSystem.ToSRGB(new Vector3(t, t * 0.5f, t * 0.25f));
        }

        sw.Stop();
        double opsPerSec = 2_000_000 / sw.Elapsed.TotalSeconds;

        TestContext?.WriteLine($"Phase4 perf | to_srgb_ops_per_sec={opsPerSec:F0} | elapsed_ms={sw.Elapsed.TotalMilliseconds:F2}");

        Assert.IsTrue(float.IsFinite(acc.X) && float.IsFinite(acc.Y) && float.IsFinite(acc.Z));
        Assert.IsTrue(opsPerSec > 0, "Throughput snapshot should be positive.");
    }

    public TestContext? TestContext { get; set; }
}
