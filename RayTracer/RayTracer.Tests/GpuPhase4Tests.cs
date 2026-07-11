using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 4 GPU-port parity tests. These pin the pure-C# volumetric replica
/// (<see cref="Phase4Reference"/>) to the <b>actual</b> CPU renderer's
/// <c>JobSystem.IntegrateVolumetricSegment</c>: the smoke/fog density field, the
/// single-scattering camera-segment march (transmittance + in-scatter), the
/// Henyey–Greenstein phase, the per-light shadow rays on the configured step
/// interval, and the moving-path half-step reduction.
///
/// A real maze render drives primary rays through the same <see cref="BVH"/> the
/// renderer uses; each camera→hit segment is integrated by both the renderer's
/// own method and the replica, and the transmittance + in-scatter compared.
/// <c>PathTracePhase4.hlsl</c> folds a line-for-line port of
/// <see cref="Phase4Reference"/> into its <c>ShadeSample</c>, so passing these
/// gives confidence in the GPU volumetric math where the GPU cannot run in CI.
/// </summary>
[TestClass]
public class GpuPhase4Tests
{
    private const int Width = 160;
    private const int Height = 120;

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

    private static JobSystem BuildJobSystem(
        Tracable[] scene, Light[] lights, Camera camera,
        SmokeMode smokeMode, VolumetricQuality quality)
        => new(
            Width, Height, scene, camera, stride: Width * 4, lights: lights,
            renderOptions: new RenderOptions(Lighting: LightingMode.NEE, MaxSampleCount: 500),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(
                CheckerboardMotion: false, EnableTaa: true, EnableDiffuseCache: false,
                SmokeMode: smokeMode,
                Volumetrics: VolumetricOptions.FromQuality(quality, smokeMode)),
            debugOptions: new DebugOptions());

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

    // ── Segment integrator parity vs the real IntegrateVolumetricSegment ──

    [TestMethod]
    // Medium: 8 steps, no shadow rays (ambient-only inscatter), isotropic phase.
    [DataRow(SmokeMode.Biome, VolumetricQuality.Medium, false, DisplayName = "biome, medium, still")]
    // High: 16 steps, shadow ray every 4th step, isotropic phase.
    [DataRow(SmokeMode.AlwaysFog, VolumetricQuality.High, false, DisplayName = "fog, high, still")]
    // Ultra + moving: 24→12 steps, shadow every step, anisotropic (HG) phase.
    [DataRow(SmokeMode.AlwaysGroundSmoke, VolumetricQuality.Ultra, true, DisplayName = "ground, ultra, moving")]
    [DataRow(SmokeMode.Biome, VolumetricQuality.Ultra, true, DisplayName = "biome, ultra, moving")]
    public void IntegrateSegment_MatchesCpu(SmokeMode smokeMode, VolumetricQuality quality, bool isMoving)
    {
        var scene = BuildMaze(out var lights, out var camera);
        var js = BuildJobSystem(scene, lights, camera, smokeMode, quality);
        js.IsMoving = isMoving;

        var tracer = new BvhSceneTracer(scene, wavelength: 555);
        var packed = LightPacker.Pack(lights);
        VolumetricOptions options = js.Volumetrics;

        float tanHalfFov = MathF.Tan(camera.Fov * 0.5f);
        float aspectTanHalfFov = camera.Aspect * tanHalfFov;
        var rot = new Vector4(camera.Rotation.X, camera.Rotation.Y, camera.Rotation.Z, camera.Rotation.W);

        int compared = 0, foggy = 0;
        for (int y = 4; y < Height; y += 9)
        {
            for (int x = 4; x < Width; x += 9)
            {
                Vector3 dir = Phase1Reference.PrimaryRayDirection(
                    x, y, 0u, rot, camera.ImgPlaneZ,
                    tanHalfFov, aspectTanHalfFov, 1f / Width, 1f / Height, subPixelJitter: false);

                if (!tracer.ClosestHit(camera.Position, dir, out _, out Vector3 hitPoint))
                    continue;

                VolumetricSample truth = js.IntegrateVolumetricSegment(camera.Position, hitPoint, dir);
                VolumetricSample got = Phase4Reference.IntegrateSegment(
                    camera.Position, hitPoint, dir, options, isMoving,
                    packed.Positions, packed.Colors, tracer);

                string at = $"px=({x},{y}) mode={smokeMode} q={quality} moving={isMoving}";
                AssertCloseF(truth.Transmittance, got.Transmittance, $"T {at}");
                AssertClose(truth.Inscatter, got.Inscatter, $"inscatter {at}");

                compared++;
                if (truth.Transmittance < 0.999f || truth.Inscatter != Vector3.Zero)
                    foggy++;
            }
        }

        Assert.IsTrue(compared > 50, $"expected many segments, got {compared}");
        Assert.IsTrue(foggy > 0, "some segments should pass through the medium (attenuation / in-scatter)");
    }

    [TestMethod]
    public void IntegrateSegment_QualityOff_IsPassthrough()
    {
        var scene = BuildMaze(out var lights, out var camera);
        // Off disables volumetrics regardless of the smoke mode requested.
        var js = BuildJobSystem(scene, lights, camera, SmokeMode.Biome, VolumetricQuality.Off);
        var packed = LightPacker.Pack(lights);

        var origin = camera.Position;
        var hit = origin + new Vector3(0f, 0f, 6f);
        var dir = Vector3.Normalize(hit - origin);

        VolumetricSample truth = js.IntegrateVolumetricSegment(origin, hit, dir);
        VolumetricSample got = Phase4Reference.IntegrateSegment(
            origin, hit, dir, js.Volumetrics, isMoving: false,
            packed.Positions, packed.Colors, occluder: null);

        Assert.AreEqual(1f, truth.Transmittance, 1e-6f, "disabled volumetrics must not attenuate");
        Assert.AreEqual(Vector3.Zero, truth.Inscatter);
        AssertCloseF(truth.Transmittance, got.Transmittance, "T off");
        AssertClose(truth.Inscatter, got.Inscatter, "inscatter off");
    }

    // ── Density field (biome banding, verified by Phase 4.2) ───────────

    [TestMethod]
    public void GetDensityBiome_IsBandedNonNegativeAndReachesZero()
    {
        float minD = float.MaxValue, maxD = float.MinValue;
        int zeroSamples = 0, total = 0;

        // Scan a world region spanning several 8×8 biome cells at mid-corridor height.
        for (float z = 0f; z < 64f; z += 2f)
        {
            for (float x = 0f; x < 64f; x += 2f)
            {
                float d = Phase4Reference.GetDensityBiome(new Vector3(x, 1.0f, z));
                Assert.IsTrue(d >= 0f, $"density must be non-negative at ({x},{z}), got {d}");
                minD = MathF.Min(minD, d);
                maxD = MathF.Max(maxD, d);
                if (d < 1e-6f)
                    zeroSamples++;
                total++;
            }
        }

        Assert.IsTrue(maxD > 1e-3f, $"biome smoke should reach a clearly positive density (max={maxD})");
        Assert.IsTrue(maxD - minD > 1e-3f, $"biome density should band across space (min={minD}, max={maxD})");
        Assert.IsTrue(zeroSamples > 0, "smoke-free biomes should produce near-zero density somewhere");
        Assert.IsTrue(zeroSamples < total, "smoke biomes should produce positive density somewhere");
    }

    [TestMethod]
    public void GetDensity_NoneIsZero()
    {
        var p = new Vector3(3f, 1f, 5f);
        Assert.AreEqual(0f, Phase4Reference.GetDensity(p, SmokeMode.None));
        Assert.IsTrue(Phase4Reference.GetDensity(p, SmokeMode.AlwaysFog) > 0f);
    }

    // ── Animation: the turbulence domain drifts with time ──────────────

    [TestMethod]
    public void SmokeTurbulence_IsStaticAtTimeZeroAndMovesOverTime()
    {
        // time = 0 must be deterministic (the static field the CPU/self-test use).
        var p = new Vector3(5f, 1f, 7f);
        Assert.AreEqual(
            Phase4Reference.SmokeTurbulence(p, 0f),
            Phase4Reference.SmokeTurbulence(p, 0f),
            "time = 0 must be static/deterministic");

        // Over a grid, most points' density must change between two times, i.e.
        // the field actually advects (a single point could coincidentally match).
        int changed = 0, total = 0;
        for (float x = 0f; x < 40f; x += 3.3f)
        {
            for (float z = 0f; z < 40f; z += 3.3f)
            {
                var q = new Vector3(x, 1f, z);
                float a = Phase4Reference.SmokeTurbulence(q, 0f);
                float b = Phase4Reference.SmokeTurbulence(q, 6f);
                if (MathF.Abs(a - b) > 1e-3f)
                    changed++;
                total++;
            }
        }
        Assert.IsTrue(changed > total / 2,
            $"the fog field should move over time ({changed}/{total} sample points changed)");
    }

    // ── Compositing identity (why the shader may correct after the march) ──

    [TestMethod]
    public void Apply_MatchesRawSpaceCompositing()
    {
        var rawXyz = new Vector3(0.3f, 0.5f, 0.2f);
        const float k = 1.37f; // stand-in for the scalar DeterministicCorrection
        var sample = new VolumetricSample(0.6f, new Vector3(0.1f, 0.2f, 0.05f));

        // CPU order: composite on raw XYZ, then apply the scalar correction.
        Vector3 rawSpace = (rawXyz * sample.Transmittance + sample.Inscatter) * k;
        // GPU/Apply order: correct first, then composite (equal because k is scalar).
        Vector3 correctedSpace = Phase4Reference.Apply(rawXyz * k, sample, k);

        AssertClose(rawSpace, correctedSpace, "compositing identity");
    }
}
