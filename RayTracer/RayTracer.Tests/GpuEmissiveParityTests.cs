using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Emissive-surface parity tests (pinball-plan §5.3, P0). These pin the pure-C# lighting replica
/// (<see cref="Phase2Reference"/> — the oracle the Phase 6 HLSL is a line-for-line port of) to the
/// actual CPU renderer (<c>JobSystem.TraceCore</c>) for a scene containing a
/// <see cref="SurfaceKind.Emissive"/> surface, over the real <see cref="BVH"/> and <see cref="Light"/>
/// array with the diffuse cache off and volumetrics off (the GPU port's path).
///
/// The scene is a quad-only box (Phase2Reference is quad-only — it shades <c>GpuPrimitive[]</c>, so no
/// analytic spheres): a mirror floor, a glowing emissive panel, and a diffuse back wall. It exercises
/// <b>both</b> emission code paths — the panel seen directly (the primary-hit emission term) and the
/// panel reflected in the mirror floor (the specular-terminal emission that puts neon in the chrome
/// ball) — and asserts the CPU and the replica agree to within FP tolerance.
/// </summary>
[TestClass]
public class GpuEmissiveParityTests
{
    private const int Width = 160;
    private const int Height = 120;

    private sealed record Scene(
        Tracable[] Tracables,
        PackedScene Packed,
        SpectralResources Res,
        Light[] Lights,
        Vector3[] LightPositions,
        Camera Camera);

    // A flat (wavelength-constant) spectral material — a neutral mirror or a plain diffuse — built
    // directly so the test does not depend on the measured material CSV.
    private static MaterialData FlatSpectral(string id, float reflectance, SurfaceKind surface)
    {
        int[] wl = [360, 830];
        float[] v = [reflectance, reflectance];
        return new MaterialData(id, id, null, null, 0f, 0f, 0f, 0f,
            reflectance, reflectance, reflectance, 0f, 0f,
            new SpectralData(wl, v, (float[])v.Clone()), surface);
    }

    private static Scene BuildScene()
    {
        var mirror = FlatSpectral("emis-mirror", 0.9f, SurfaceKind.Mirror);
        var diffuse = FlatSpectral("emis-diffuse", 0.6f, SurfaceKind.Diffuse);
        var neon = MaterialData.Emissive("emis-neon", new Vector3(0.2f, 1.0f, 0.4f), radiance: 3.0f);

        var scene = new Tracable[]
        {
            // Mirror floor (y=0), facing up. Downward camera rays reflect up onto the emissive panel.
            new TracableRectangle((new Vector3(-8f, 0f, -2f), new Vector3(8f, 0f, -2f), new Vector3(-8f, 0f, 11f)), mirror),
            // Emissive neon panel at z=10, a partial-width vertical quad so some rays pass it to the wall.
            new TracableRectangle((new Vector3(-3f, 0.5f, 10f), new Vector3(3f, 0.5f, 10f), new Vector3(-3f, 6f, 10f)), neon),
            // Diffuse back wall at z=11 (lit by the point light) so the NEE/indirect path is also compared.
            new TracableRectangle((new Vector3(-8f, 0f, 11f), new Vector3(8f, 0f, 11f), new Vector3(-8f, 10f, 11f)), diffuse),
        };

        var lights = new[]
        {
            new Light { Position = new Vector3(0f, 6f, 3f), Color = new Vector3(1f, 1f, 1f), Ambient = 0f, Radius = 0f },
        };

        var wl = new WavelengthLookup();
        var packed = GpuScenePacker.Pack(scene);
        var res = SpectralResourceBaker.Bake(wl, packed.Materials);
        var packedLights = LightPacker.Pack(lights);

        var camera = new Camera
        {
            Position = new Vector3(0f, 3f, -5f),
            Rotation = Quaternion.Identity, // looks toward +Z
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
            denoiseOptions: new DenoiseOptions(EnableDiffuseCache: false, SmokeMode: SmokeMode.None, SampleClamp: 0f),
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

    [TestMethod]
    public void ShadeSample_MatchesTraceCore_Emissive()
    {
        var s = BuildScene();
        var js = BuildGroundTruth(s, LightingMode.NEE);
        var tracer = new BvhSceneTracer(s.Tracables, s.Res.DeterWavelengths[0]);

        // Upper image band (py > 0) sees the panel / wall directly; lower band (py < 0) sees the mirror
        // floor, which reflects the panel. Counting emission-lit pixels in each band proves both the
        // primary-hit and the specular-terminal emission paths fire (not just one).
        int compared = 0, primaryGlow = 0, reflectedGlow = 0;
        for (uint sampleIdx = 0; sampleIdx < 3; sampleIdx++)
        {
            for (int y = 3; y < Height; y += 5)
            {
                for (int x = 3; x < Width; x += 5)
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
                    // The only radiance source in this scene is the emissive panel, so any appreciable
                    // green brightness is emission (directly or via the mirror). Green channel isolates
                    // the neon-green panel from FP noise.
                    if (refTotal.Y > 1e-3f)
                    {
                        if (y < Height * 0.45f) primaryGlow++;
                        else if (y > Height * 0.55f) reflectedGlow++;
                    }
                }
            }
        }

        Assert.IsTrue(compared > 200, $"expected many surface comparisons, got {compared}");
        Assert.IsTrue(primaryGlow > 0, "the emissive panel must be visible directly (primary-hit emission path)");
        Assert.IsTrue(reflectedGlow > 0, "the emissive panel must appear in the mirror floor (specular-terminal emission path)");
    }

    [TestMethod]
    public void Emissive_Glows_WithoutAnyLight()
    {
        // With NO lights at all, an emissive surface still emits (its radiance is a source, not a
        // reflection): the primary-hit and reflected totals must be identical between CPU and replica,
        // and nonzero on the panel — proving emission is independent of the lighting path.
        var s = BuildScene() with { Lights = [], LightPositions = [] };
        var js = BuildGroundTruth(s, LightingMode.NEE);
        var tracer = new BvhSceneTracer(s.Tracables, s.Res.DeterWavelengths[0]);

        int compared = 0, glow = 0;
        for (int y = 3; y < Height; y += 4)
        {
            for (int x = 3; x < Width; x += 4)
            {
                Vector3 dir = PrimaryDir(s.Camera, x, y);
                if (!tracer.ClosestHit(s.Camera.Position, dir, out _, out _))
                    continue;

                var truth = TraceOneSample(js, s.Camera, x, y, 0u);
                Vector3 refTotal = Phase2Reference.ShadeSample(
                    tracer, s.Packed.Primitives, s.Res, [], LightingMode.NEE,
                    s.Camera.Position, dir, Phase1Reference.Hash2D(x, y), 0u, out _, out _);

                AssertClose(truth.total, refTotal, $"total px=({x},{y})");
                compared++;
                if (refTotal.Y > 1e-3f) glow++;
            }
        }

        Assert.IsTrue(compared > 100, $"expected surface comparisons, got {compared}");
        Assert.IsTrue(glow > 0, "the emissive panel must glow with no lights present");
    }
}
