using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// GPU-port parity for Phase 1.1 mirrors (spectral-effects-plan.md). Pins the
/// pure-C# GPU replica (<see cref="Phase2Reference.ShadeSample"/>, whose mirror
/// branch the HLSL <c>PathTracePhase*.hlsl</c> shaders port line-for-line) to the
/// CPU renderer's own <c>JobSystem.TraceCore</c> mirror output, over a scene whose
/// primary rays all strike a mirror that reflects a lit, coloured wall. Because CI
/// has no GPU, this replica parity is the contract the shaders must match.
/// </summary>
[TestClass]
public sealed class GpuMirrorTests
{
    private const int Width = 64;
    private const int Height = 48;

    // ── Scene building ─────────────────────────────────────────────────

    private static SpectralData Spectrum(Func<int, float> reflectance)
    {
        var wavelengths = new List<int>();
        var values = new List<float>();
        for (int w = 360; w <= 830; w += 5)
        {
            wavelengths.Add(w);
            values.Add(reflectance(w));
        }

        float[] v = values.ToArray();
        return new SpectralData(wavelengths.ToArray(), v, (float[])v.Clone());
    }

    // Distinct ids matter here: GpuScenePacker de-duplicates materials by Id, so
    // the mirror and the wall must not collide (the CPU uses object references and
    // would not notice).
    private static MaterialData Material(string id, SurfaceKind kind, SpectralData spectrum)
        => new(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, spectrum, kind);

    private static TracableRectangle Wall(float zPlane, bool normalTowardNegZ, MaterialData material)
    {
        const float e = 20f;
        Vector3 l1 = new(-e, -e, zPlane);
        Vector3 l2 = normalTowardNegZ ? new Vector3(-e, e, zPlane) : new Vector3(e, -e, zPlane);
        Vector3 l3 = normalTowardNegZ ? new Vector3(e, -e, zPlane) : new Vector3(-e, e, zPlane);
        return new TracableRectangle((l1, l2, l3), material);
    }

    private sealed record Scene(
        Tracable[] Tracables, PackedScene Packed, SpectralResources Res,
        Light[] Lights, Vector3[] LightPositions, Camera Camera);

    private static Scene Build()
    {
        var mirror = Material("mirror", SurfaceKind.Mirror, Spectrum(_ => 0.9f));
        var wall = Material("wall", SurfaceKind.Diffuse,
            Spectrum(w => 0.05f + 0.80f * Math.Clamp((w - 500f) / 150f, 0f, 1f)));

        var scene = new Tracable[]
        {
            Wall(5f, normalTowardNegZ: true, mirror),   // in front; primary rays hit this
            Wall(-5f, normalTowardNegZ: false, wall),   // behind camera; the reflection target
        };

        // A light on the wall's viewing side so the reflected surface is lit (NEE).
        var lights = new[] { new Light { Position = new Vector3(1.5f, 1.0f, -3.5f), Color = Vector3.One } };

        var wl = new WavelengthLookup();
        var packed = GpuScenePacker.Pack(scene);
        var res = SpectralResourceBaker.Bake(wl, packed.Materials);
        var packedLights = LightPacker.Pack(lights);

        var camera = new Camera
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity, // looks down +Z
            Fov = MathF.PI / 3f,
            Aspect = (float)Width / Height,
            ImgPlaneZ = 1f,
        };

        return new Scene(scene, packed, res, lights, packedLights.Positions, camera);
    }

    private static JobSystem BuildGroundTruth(Scene s, LightingMode mode)
        => new(
            Width, Height, s.Tracables, s.Camera, stride: Width * 4, lights: s.Lights,
            renderOptions: new RenderOptions(Lighting: mode, MaxSampleCount: 500),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(EnableDiffuseCache: false, SmokeMode: SmokeMode.None),
            debugOptions: new DebugOptions());

    private static Vector3 PrimaryDir(Camera cam, int x, int y)
    {
        float tanHalfFov = MathF.Tan(cam.Fov * 0.5f);
        float aspectTanHalfFov = cam.Aspect * tanHalfFov;
        float px = (2f * ((x + 0.5f) / Width) - 1f) * aspectTanHalfFov;
        float py = (1f - 2f * ((y + 0.5f) / Height)) * tanHalfFov;
        var localDir = new Vector3(px, py, cam.ImgPlaneZ);
        return Vector3.Normalize(Vector3.Transform(localDir, cam.Rotation));
    }

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
        for (int c = 0; c < 3; c++)
        {
            float e = c == 0 ? expected.X : c == 1 ? expected.Y : expected.Z;
            float a = c == 0 ? actual.X : c == 1 ? actual.Y : actual.Z;
            float tol = 1e-3f + 1e-3f * MathF.Abs(e);
            Assert.IsTrue(MathF.Abs(e - a) <= tol,
                $"{what}[{c}]: expected {e}, got {a} (|Δ|={MathF.Abs(e - a)} > {tol})");
        }
    }

    // ── Tests ──────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(LightingMode.NEE, DisplayName = "NEE")]
    [DataRow(LightingMode.Direct, DisplayName = "Direct")]
    [DataRow(LightingMode.None, DisplayName = "None")]
    public void MirrorShadeSample_MatchesTraceCore(LightingMode mode)
    {
        var s = Build();
        var js = BuildGroundTruth(s, mode);
        var tracer = new BvhSceneTracer(s.Tracables, s.Res.DeterWavelengths[0]);

        int compared = 0, mirrored = 0, lit = 0;
        for (uint sampleIdx = 0; sampleIdx < 3; sampleIdx++)
        {
            for (int y = 4; y < Height; y += 7)
            {
                for (int x = 4; x < Width; x += 7)
                {
                    Vector3 dir = PrimaryDir(s.Camera, x, y);
                    if (!tracer.ClosestHit(s.Camera.Position, dir, out int quad, out _))
                        continue;

                    // Only the mirror wall should be a primary hit here.
                    if (s.Packed.Primitives[quad].Surface == (uint)SurfaceKind.Mirror)
                        mirrored++;

                    var truth = TraceOneSample(js, s.Camera, x, y, sampleIdx);

                    uint pixelHash = Phase1Reference.Hash2D(x, y);
                    Vector3 refTotal = Phase2Reference.ShadeSample(
                        tracer, s.Packed.Primitives, s.Res, s.LightPositions, mode,
                        s.Camera.Position, dir, pixelHash, sampleIdx,
                        out Vector3 refDirect, out Vector3 refIndirect);

                    string at = $"px=({x},{y}) s={sampleIdx} mode={mode}";
                    AssertClose(truth.total, refTotal, $"total {at}");
                    AssertClose(truth.direct, refDirect, $"direct {at}");
                    AssertClose(truth.indirect, refIndirect, $"indirect {at}");

                    compared++;
                    if (refTotal.Y > 1e-4f) lit++;
                }
            }
        }

        Assert.IsTrue(compared > 50, $"expected many comparisons, got {compared}");
        Assert.IsTrue(mirrored > 50, $"primary rays should strike the mirror, got {mirrored}");
        Assert.IsTrue(lit > 0, "mirror reflection of the lit wall should be non-black somewhere");
    }
}
