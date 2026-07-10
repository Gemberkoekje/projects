using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 2 GPU-port parity tests. These pin the pure-C# lighting replica
/// (<see cref="Phase2Reference"/>) to the <b>actual</b> CPU renderer output —
/// one sample of <c>JobSystem.TraceCore</c> — over the real <see cref="BVH"/> and
/// <see cref="Light"/> array, with the diffuse cache disabled and volumetrics off
/// (matching the GPU port's cache-free path). The HLSL Phase 2 shader is a
/// line-for-line port of <see cref="Phase2Reference"/>, so passing these gives
/// confidence in the GPU lighting math where the GPU itself cannot run in CI.
///
/// Both the replica (via <see cref="BvhSceneTracer"/>) and <c>TraceCore</c> draw
/// their ray hits, shadow tests, and bounces from BVHs built from the same scene,
/// so the comparison isolates the shading arithmetic and is essentially exact.
/// </summary>
[TestClass]
public class GpuPhase2Tests
{
    private const int Width = 160;
    private const int Height = 120;

    private sealed record Scene(
        Tracable[] Tracables,
        WavelengthLookup Wl,
        PackedScene Packed,
        SpectralResources Res,
        Light[] Lights,
        Vector3[] LightPositions,
        Camera Camera);

    private static Scene BuildScene(int lightSeed = 4242)
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(8, 8, seed: 12345);

        materials.TryGetMaterial("01138", out var goal);
        materials.TryGetMaterial("01085", out var mortar);
        materials.TryGetMaterial("01138", out var grid);

        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01138"],
            ceilingMaterial: materials["01085"],
            mortarMaterial: mortar,
            ceilingGridMaterial: grid,
            goalMaterial: goal);

        var lights = MazeGeometryBuilder.BuildLights(maze, lightSpawnChance: 0.5f, biomeSize: 4, seed: lightSeed);

        var wl = new WavelengthLookup();
        var packed = GpuScenePacker.Pack(scene);
        var res = SpectralResourceBaker.Bake(wl, packed.Materials);
        var packedLights = LightPacker.Pack(lights);

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        var camera = new Camera
        {
            Position = new Vector3(cs * 0.5f, wh * 0.5f, cs * 0.5f),
            Rotation = CameraController.HeadingToQuaternion(Direction.South),
            Fov = MathF.PI / 3f,
            Aspect = (float)Width / Height,
            ImgPlaneZ = 1f,
        };

        return new Scene(scene, wl, packed, res, lights, packedLights.Positions, camera);
    }

    private static JobSystem BuildGroundTruth(Scene s, LightingMode mode, float sampleClamp)
        => new(
            Width, Height, s.Tracables, s.Camera, stride: Width * 4, lights: s.Lights,
            renderOptions: new RenderOptions(Lighting: mode, MaxSampleCount: 500),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(EnableDiffuseCache: false, SmokeMode: SmokeMode.None, SampleClamp: sampleClamp),
            debugOptions: new DebugOptions());

    // Exact TraceCore ray-gen (jitter off): builds a bit-identical primary ray so
    // the replica and TraceCore trace the same ray into equal BVHs.
    private static Vector3 PrimaryDir(Camera cam, int x, int y)
    {
        float tanHalfFov = MathF.Tan(cam.Fov * 0.5f);
        float aspectTanHalfFov = cam.Aspect * tanHalfFov;
        float invWidth = 1f / Width;
        float invHeight = 1f / Height;
        float px = (2f * ((x + 0.5f) * invWidth) - 1f) * aspectTanHalfFov;
        float py = (1f - 2f * ((y + 0.5f) * invHeight)) * tanHalfFov;
        var localDir = new Vector3(px, py, cam.ImgPlaneZ);
        return Vector3.Normalize(Vector3.Transform(localDir, cam.Rotation));
    }

    // Renders exactly one fresh sample of pixel (x,y) with the given sample index,
    // returning TraceCore's corrected total / direct / indirect for that sample.
    private static (Vector3 total, Vector3 direct, Vector3 indirect) TraceOneSample(
        JobSystem js, Camera cam, int x, int y, uint sampleIdx)
    {
        int ix = y * Width + x;
        js.AccumXYZ[ix] = Vector3.Zero;
        js.SampleCount[ix] = 0;
        js.Debug.DirectLightingXYZ[ix] = Vector3.Zero;
        js.Debug.IndirectLightingXYZ[ix] = Vector3.Zero;
        js.WavelengthCounter[ix] = sampleIdx;
        js.TraceCore(cam, y, x);
        return (js.AccumXYZ[ix], js.Debug.DirectLightingXYZ[ix], js.Debug.IndirectLightingXYZ[ix]);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual, string what)
    {
        AssertCloseF(expected.X, actual.X, what + ".X");
        AssertCloseF(expected.Y, actual.Y, what + ".Y");
        AssertCloseF(expected.Z, actual.Z, what + ".Z");
    }

    private static void AssertCloseF(float expected, float actual, string what)
    {
        float tol = 1e-3f + 1e-3f * MathF.Abs(expected);
        Assert.IsTrue(MathF.Abs(expected - actual) <= tol,
            $"{what}: expected {expected}, got {actual} (|Δ|={MathF.Abs(expected - actual)} > {tol})");
    }

    // ── End-to-end NEE parity ──────────────────────────────────────────

    [TestMethod]
    public void ShadeSample_MatchesTraceCore_NEE()
    {
        var s = BuildScene();
        Assert.IsTrue(s.Lights.Length > 0, "scene should contain lights so the NEE path is exercised");

        var js = BuildGroundTruth(s, LightingMode.NEE, sampleClamp: 0f);
        var tracer = new BvhSceneTracer(s.Tracables, s.Res.DeterWavelengths[0]);

        int compared = 0, lit = 0, indirectNonZero = 0;
        for (uint sampleIdx = 0; sampleIdx < 3; sampleIdx++)
        {
            for (int y = 6; y < Height; y += 11)
            {
                for (int x = 6; x < Width; x += 11)
                {
                    Vector3 dir = PrimaryDir(s.Camera, x, y);
                    if (!tracer.ClosestHit(s.Camera.Position, dir, out _, out _))
                        continue;

                    var truth = TraceOneSample(js, s.Camera, x, y, sampleIdx);

                    uint pixelHash = Phase1Reference.Hash2D(x, y);
                    Vector3 refTotal = Phase2Reference.ShadeSample(
                        tracer, s.Packed.Primitives, s.Res, s.LightPositions, LightingMode.NEE,
                        s.Camera.Position, dir, pixelHash, sampleIdx,
                        out Vector3 refDirect, out Vector3 refIndirect);

                    string at = $"px=({x},{y}) s={sampleIdx}";
                    AssertClose(truth.total, refTotal, $"total {at}");
                    AssertClose(truth.direct, refDirect, $"direct {at}");
                    AssertClose(truth.indirect, refIndirect, $"indirect {at}");

                    compared++;
                    if (refDirect.Y > 1e-4f) lit++;
                    if (refIndirect.LengthSquared() > 1e-10f) indirectNonZero++;
                }
            }
        }

        Assert.IsTrue(compared > 50, $"expected many surface comparisons, got {compared}");
        Assert.IsTrue(lit > 0, "some surface pixels should receive direct light (NEE path exercised)");
        Assert.IsTrue(indirectNonZero > 0, "some surface pixels should receive indirect light (bounce path exercised)");
    }

    // ── Direct mode (no primary shadow ray) ────────────────────────────

    [TestMethod]
    public void ShadeSample_MatchesTraceCore_Direct()
    {
        var s = BuildScene();
        var js = BuildGroundTruth(s, LightingMode.Direct, sampleClamp: 0f);
        var tracer = new BvhSceneTracer(s.Tracables, s.Res.DeterWavelengths[0]);

        int compared = 0;
        for (int y = 8; y < Height; y += 13)
        {
            for (int x = 8; x < Width; x += 13)
            {
                Vector3 dir = PrimaryDir(s.Camera, x, y);
                if (!tracer.ClosestHit(s.Camera.Position, dir, out _, out _))
                    continue;

                var truth = TraceOneSample(js, s.Camera, x, y, 0u);

                Vector3 refTotal = Phase2Reference.ShadeSample(
                    tracer, s.Packed.Primitives, s.Res, s.LightPositions, LightingMode.Direct,
                    s.Camera.Position, dir, Phase1Reference.Hash2D(x, y), 0u,
                    out Vector3 refDirect, out Vector3 refIndirect);

                string at = $"px=({x},{y})";
                AssertClose(truth.total, refTotal, $"total {at}");
                AssertClose(truth.direct, refDirect, $"direct {at}");
                AssertClose(truth.indirect, refIndirect, $"indirect {at}");
                compared++;
            }
        }

        Assert.IsTrue(compared > 20, $"expected surface comparisons, got {compared}");
    }

    // ── None mode == fullbright (and == Phase1Reference.ShadeHit) ───────

    [TestMethod]
    public void ShadeSample_None_MatchesFullbright()
    {
        var s = BuildScene();
        var js = BuildGroundTruth(s, LightingMode.None, sampleClamp: 0f);
        var tracer = new BvhSceneTracer(s.Tracables, s.Res.DeterWavelengths[0]);

        int compared = 0;
        for (int y = 8; y < Height; y += 13)
        {
            for (int x = 8; x < Width; x += 13)
            {
                Vector3 dir = PrimaryDir(s.Camera, x, y);
                if (!tracer.ClosestHit(s.Camera.Position, dir, out int quad, out Vector3 hitPoint))
                    continue;

                var truth = TraceOneSample(js, s.Camera, x, y, 0u);

                uint pixelHash = Phase1Reference.Hash2D(x, y);
                Vector3 refTotal = Phase2Reference.ShadeSample(
                    tracer, s.Packed.Primitives, s.Res, s.LightPositions, LightingMode.None,
                    s.Camera.Position, dir, pixelHash, 0u,
                    out Vector3 refDirect, out Vector3 refIndirect);

                // Fullbright: total == direct == Phase 1 ShadeHit; no indirect.
                Vector3 fullbright = Phase1Reference.ShadeHit(s.Packed.Primitives[quad], s.Res, dir, hitPoint, pixelHash, 0u);

                string at = $"px=({x},{y})";
                AssertClose(truth.total, refTotal, $"total {at}");
                AssertClose(fullbright, refTotal, $"fullbright {at}");
                AssertClose(refTotal, refDirect, $"direct==total {at}");
                Assert.AreEqual(0f, refIndirect.LengthSquared(), 1e-12f, $"no indirect {at}");
                compared++;
            }
        }

        Assert.IsTrue(compared > 20, $"expected surface comparisons, got {compared}");
    }

    // ── Sample clamping ────────────────────────────────────────────────

    [TestMethod]
    public void ShadeSample_ClampedTotal_MatchesTraceCore()
    {
        const float clamp = 0.5f;
        var s = BuildScene();
        var js = BuildGroundTruth(s, LightingMode.NEE, sampleClamp: clamp);
        var tracer = new BvhSceneTracer(s.Tracables, s.Res.DeterWavelengths[0]);
        var clampVec = new Vector3(clamp);

        int compared = 0, actuallyClamped = 0;
        for (int y = 6; y < Height; y += 11)
        {
            for (int x = 6; x < Width; x += 11)
            {
                Vector3 dir = PrimaryDir(s.Camera, x, y);
                if (!tracer.ClosestHit(s.Camera.Position, dir, out _, out _))
                    continue;

                var truth = TraceOneSample(js, s.Camera, x, y, 0u);

                Vector3 refTotal = Phase2Reference.ShadeSample(
                    tracer, s.Packed.Primitives, s.Res, s.LightPositions, LightingMode.NEE,
                    s.Camera.Position, dir, Phase1Reference.Hash2D(x, y), 0u, out _, out _);
                Vector3 refClamped = Vector3.Clamp(refTotal, Vector3.Zero, clampVec);

                AssertClose(truth.total, refClamped, $"clamped total px=({x},{y})");
                if (refTotal.X > clamp || refTotal.Y > clamp || refTotal.Z > clamp)
                    actuallyClamped++;
                compared++;
            }
        }

        Assert.IsTrue(compared > 20, $"expected surface comparisons, got {compared}");
        Assert.IsTrue(actuallyClamped > 0, "at least one bright pixel should exceed the clamp so clamping is exercised");
    }
}
