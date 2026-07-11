using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 3 GPU-port parity tests. These pin the pure-C# temporal-resolve replica
/// (<see cref="Phase3Reference"/>) to the <b>actual</b> CPU renderer's
/// <c>JobSystem.TaaResolver</c> output: the bilateral spatial filter, the
/// previous-frame reprojection + bilinear history fetch, disocclusion / normal
/// rejection, neighborhood-clamped temporal blend, and history weight.
///
/// A real two-frame render drives the CPU pipeline so that the accumulation
/// buffer, G-buffer (hit point / normal / hit mask) and TAA history are populated
/// exactly as they are in production; the replica is then fed those same buffers
/// and its per-pixel output compared against what the resolver actually wrote to
/// its TAA-next buffer. <c>ResolvePhase3.hlsl</c> is a line-for-line port of
/// <see cref="Phase3Reference"/>, so passing these gives confidence in the GPU
/// temporal math where the GPU itself cannot run in CI.
/// </summary>
[TestClass]
public class GpuPhase3Tests
{
    private const int Width = 160;
    private const int Height = 120;
    // Small enough that the ×10 motion boost (clamp(alpha*10)) stays below 1, so
    // the moving path still produces a nonzero history weight (1 - alpha) to test.
    private const float TemporalAlpha = 0.05f;

    private static Tracable[] BuildMaze(out Light[] lights, out Camera camera)
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

        lights = MazeGeometryBuilder.BuildLights(maze, lightSpawnChance: 0.5f, biomeSize: 4, seed: 4242);

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        camera = new Camera
        {
            Position = new Vector3(cs * 0.5f, wh * 0.5f, cs * 0.5f),
            Rotation = CameraController.HeadingToQuaternion(Direction.South),
            Fov = MathF.PI / 3f,
            Aspect = (float)Width / Height,
            ImgPlaneZ = 1f,
        };
        return scene;
    }

    private static JobSystem BuildJobSystem(Tracable[] scene, Light[] lights, Camera camera,
        int filterRadius, bool edgeAware)
        => new(
            Width, Height, scene, camera, stride: Width * 4, lights: lights,
            renderOptions: new RenderOptions(Lighting: LightingMode.NEE, MaxSampleCount: 500),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(
                FilterRadius: filterRadius, EdgeAwareFilter: edgeAware, CheckerboardMotion: false,
                TemporalBlendAlpha: TemporalAlpha, EnableTaa: true,
                EnableDiffuseCache: false, SmokeMode: SmokeMode.None),
            debugOptions: new DebugOptions());

    // Trace every pixel once (one fresh sample per pixel), then run the real TAA
    // resolve. Mirrors one production frame end-to-end.
    private static void RenderFrame(JobSystem js, Camera camera, bool isMoving)
    {
        js.IsMoving = isMoving;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                js.TraceCore(camera, y, x);
        js.ResolveDisplayBufferWithTaa();
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

    // ── End-to-end resolve parity vs the real TaaResolver ──────────────

    [TestMethod]
    [DataRow(true, false, DisplayName = "moving, box filter")]
    [DataRow(false, false, DisplayName = "still, no filter")]
    [DataRow(true, true, DisplayName = "moving, edge-aware filter")]
    public void ResolvePixel_MatchesTaaResolver(bool isMoving, bool edgeAware)
    {
        var scene = BuildMaze(out var lights, out var camera);
        var js = BuildJobSystem(scene, lights, camera, filterRadius: 1, edgeAware: edgeAware);

        // Frame 0 establishes the history buffer + previous camera. No reprojection
        // happens yet (no previous camera), so this just seeds history == resolved.
        RenderFrame(js, camera, isMoving: false);

        // Snapshot the history + previous camera the frame-1 resolve will consume,
        // before that resolve overwrites the history with its own output.
        Vector3[] histXyz = (Vector3[])js.History.HistoryXYZ.Clone();
        Vector3[] histHit = (Vector3[])js.History.HistoryHitPoint.Clone();
        bool[] histValid = (bool[])js.History.HistoryValid.Clone();
        Vector3 prevCamPos = camera.Position;
        Quaternion prevCamRot = camera.Rotation;

        // Frame 1: a second sample per pixel (accumulation moves), same camera so
        // reprojection is (near-)identity and the accept + clamp + blend path runs.
        js.IsMoving = isMoving;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                js.TraceCore(camera, y, x);

        // The buffers the resolve reads for the *current* frame.
        Vector3[] accum = (Vector3[])js.PerPixel.AccumXYZ.Clone();
        Vector3[] hitPoint = (Vector3[])js.PerPixel.HitPointWorld.Clone();
        Vector3[] normal = (Vector3[])js.Debug.NormalWorld.Clone();
        bool[] lastHit = (bool[])js.PerPixel.LastHit.Clone();

        js.ResolveDisplayBufferWithTaa();

        float tanHalfFov = MathF.Tan(camera.Fov * 0.5f);
        float aspectTanHalfFov = camera.Aspect * tanHalfFov;

        int compared = 0, blended = 0;
        for (int y = 3; y < Height; y += 7)
        {
            for (int x = 3; x < Width; x += 7)
            {
                int ix = y * Width + x;

                Vector3 got = Phase3Reference.ResolvePixel(
                    accum, hitPoint, normal, lastHit, Width, Height, x, y,
                    histXyz, histHit, histValid, prevCamPos, prevCamRot,
                    tanHalfFov, aspectTanHalfFov,
                    useTaa: true, isMoving: isMoving, filterRadius: 1, edgeAware: edgeAware,
                    temporalBlendAlpha: TemporalAlpha,
                    out float gotWeight, out _);

                Vector3 truth = js.History.NextXYZ[ix];
                float truthWeight = js.Debug.HistoryWeight[ix];

                string at = $"px=({x},{y}) moving={isMoving} edge={edgeAware}";
                AssertClose(truth, got, $"resolved {at}");
                AssertCloseF(truthWeight, gotWeight, $"historyWeight {at}");

                compared++;
                if (gotWeight > 0f)
                    blended++;
            }
        }

        Assert.IsTrue(compared > 50, $"expected many comparisons, got {compared}");
        Assert.IsTrue(blended > 0,
            "some surface pixels should blend with accepted history (TAA reprojection path exercised)");
    }

    // ── Bilateral spatial filter (isolated) ────────────────────────────

    [TestMethod]
    public void FilteredXYZ_RadiusZero_ReturnsUnfiltered()
    {
        var accum = new Vector3[4];
        accum[0] = new Vector3(0.1f, 0.2f, 0.3f);
        accum[3] = new Vector3(9f, 9f, 9f);
        Vector3 v = Phase3Reference.FilteredXYZ(accum, 2, 2, 0, 0, radius: 0, edgeAware: false);
        Assert.AreEqual(accum[0], v);
    }

    [TestMethod]
    public void FilteredXYZ_Box_AveragesNeighborhood()
    {
        // 3×3 constant field → box filter is the identity of the constant.
        int w = 3, h = 3;
        var accum = new Vector3[w * h];
        for (int i = 0; i < accum.Length; i++)
            accum[i] = new Vector3(0.5f, 0.25f, 0.75f);
        accum[4] = new Vector3(1f, 1f, 1f); // center differs

        Vector3 box = Phase3Reference.FilteredXYZ(accum, w, h, 1, 1, radius: 1, edgeAware: false);
        // mean of eight 0.5s + one 1.0 over 9 samples, per component.
        float expX = (8 * 0.5f + 1f) / 9f;
        Assert.AreEqual(expX, box.X, 1e-6f);
    }

    [TestMethod]
    public void FilteredXYZ_EdgeAware_PreservesBrightCenter()
    {
        // A bright center surrounded by dark neighbors: the luminance-weighted
        // filter should pull the result toward the center, not the box mean.
        int w = 3, h = 3;
        var accum = new Vector3[w * h];
        for (int i = 0; i < accum.Length; i++)
            accum[i] = new Vector3(0f, 0f, 0f);
        accum[4] = new Vector3(1f, 1f, 1f);

        Vector3 box = Phase3Reference.FilteredXYZ(accum, w, h, 1, 1, radius: 1, edgeAware: false);
        Vector3 edge = Phase3Reference.FilteredXYZ(accum, w, h, 1, 1, radius: 1, edgeAware: true);

        Assert.IsTrue(edge.Y > box.Y,
            $"edge-aware should preserve the bright center more than the box filter (edge={edge.Y}, box={box.Y})");
    }

    // ── Reprojection (isolated) ────────────────────────────────────────

    [TestMethod]
    public void TryProjectToPrevPixel_IsIdentityForSameCamera()
    {
        var camera = new Camera
        {
            Position = new Vector3(1f, 2f, 3f),
            Rotation = CameraController.HeadingToQuaternion(Direction.East),
            Fov = MathF.PI / 3f,
            Aspect = (float)Width / Height,
            ImgPlaneZ = 1f,
        };
        float tanHalfFov = MathF.Tan(camera.Fov * 0.5f);
        float aspectTanHalfFov = camera.Aspect * tanHalfFov;

        // Build the world point that a ray through pixel (x,y) center hits at t.
        const int x = 90, y = 40;
        float px = (2f * ((x + 0.5f) / Width) - 1f) * aspectTanHalfFov;
        float py = (1f - 2f * ((y + 0.5f) / Height)) * tanHalfFov;
        var localDir = new Vector3(px, py, camera.ImgPlaneZ);
        Vector3 dir = Vector3.Normalize(Vector3.Transform(localDir, camera.Rotation));
        Vector3 world = camera.Position + dir * 4.2f;

        bool ok = Phase3Reference.TryProjectToPrevPixel(
            world, camera.Position, camera.Rotation, tanHalfFov, aspectTanHalfFov,
            Width, Height, out float fx, out float fy);

        Assert.IsTrue(ok, "point in front of the camera should project inside the frame");
        Assert.AreEqual(x, fx, 1e-2f, "reprojected x should map back to the source pixel");
        Assert.AreEqual(y, fy, 1e-2f, "reprojected y should map back to the source pixel");
    }

    [TestMethod]
    public void TryProjectToPrevPixel_RejectsPointsBehindCamera()
    {
        var camera = new Camera
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 3f,
            Aspect = (float)Width / Height,
            ImgPlaneZ = 1f,
        };
        float tanHalfFov = MathF.Tan(camera.Fov * 0.5f);
        float aspectTanHalfFov = camera.Aspect * tanHalfFov;

        // Camera forward is +Z; a point at -Z is behind it.
        bool ok = Phase3Reference.TryProjectToPrevPixel(
            new Vector3(0f, 0f, -5f), camera.Position, camera.Rotation,
            tanHalfFov, aspectTanHalfFov, Width, Height, out _, out _);

        Assert.IsFalse(ok, "a point behind the camera must not reproject");
    }
}
