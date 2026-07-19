using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Mover (dynamic-tier) parity tests (pinball-plan §4.1, P1). Pins the pure-C# lighting replica
/// (<see cref="Phase2Reference"/> — the oracle the Phase 6 HLSL is a line-for-line port of) to the CPU
/// renderer (<c>JobSystem.TraceCore</c>) for a scene containing <c>Dynamic</c> primitives that take the
/// cheap mover shading tier: a diffuse mover (direct + ambient only, no spectral indirect) and a mirror
/// mover (specular chain capped to <see cref="Phase2Reference.MoverSpecularBounces"/>).
///
/// Quad-only (Phase2Reference shades <c>GpuPrimitive[]</c>, not analytic spheres), so the movers here are
/// a Dynamic diffuse panel and a Dynamic mirror floor patch. Asserts (1) CPU ↔ replica agree on the cheap
/// branch, and (2) the cheap branch is non-trivial — a Dynamic primitive renders differently from the same
/// primitive left static (proving the branch is exercised, not a no-op).
/// </summary>
[TestClass]
public class GpuMoverParityTests
{
    private const int Width = 160;
    private const int Height = 120;

    private static MaterialData FlatSpectral(string id, float reflectance, SurfaceKind surface)
    {
        int[] wl = [360, 830];
        float[] v = [reflectance, reflectance];
        return new MaterialData(id, id, null, null, 0f, 0f, 0f, 0f,
            reflectance, reflectance, reflectance, 0f, 0f,
            new SpectralData(wl, v, (float[])v.Clone()), surface);
    }

    private static TracableRectangle Floor(float y, float x0, float x1, float z0, float z1, MaterialData m, bool dyn = false)
        => new((new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x0, y, z1)), m) { Dynamic = dyn };
    private static TracableRectangle WallZ(float z, float x0, float x1, float y0, float y1, MaterialData m)
        => new((new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x0, y1, z)), m);
    private static TracableRectangle WallX(float x, float z0, float z1, float y0, float y1, MaterialData m)
        => new((new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, y1, z0)), m);
    private static TracableRectangle Panel(Vector3 baseL, Vector3 baseR, float h, MaterialData m, bool dyn)
        => new((baseL, baseR, baseL + new Vector3(0f, h, 0f)), m) { Dynamic = dyn };

    // Same geometry, with the two mover panels either Dynamic (dyn=true) or static (dyn=false).
    private static (Tracable[] tracables, PackedScene packed) BuildScene(bool dyn)
    {
        var gray = FlatSpectral("mv-gray", 0.5f, SurfaceKind.Diffuse);
        var mirror = FlatSpectral("mv-mir", 0.9f, SurfaceKind.Mirror);
        var flip = FlatSpectral("mv-flip", 0.6f, SurfaceKind.Diffuse);
        var scene = new List<Tracable>
        {
            Floor(0f, -5f, 5f, 0f, 13f, gray),        // floor
            Floor(7f, -5f, 5f, 0f, 13f, gray),        // ceiling (indirect source)
            WallZ(13f, -5f, 5f, 0f, 7f, gray),        // back wall (indirect source)
            WallX(-5f, 0f, 13f, 0f, 7f, mirror),      // left: STATIC mirror (specular for the mover-mirror to reflect)
            WallX(5f, 0f, 13f, 0f, 7f, gray),         // right wall
            Panel(new Vector3(-3.4f, 0.3f, 7f), new Vector3(-0.8f, 0.3f, 7f), 3.2f, flip, dyn), // Dynamic diffuse panel
            Floor(0.02f, 0.7f, 3.6f, 4f, 8f, mirror, dyn),                                       // Dynamic mirror floor patch
        };
        return (scene.ToArray(), GpuScenePacker.Pack(scene));
    }

    private static JobSystem BuildGroundTruth(Tracable[] tracables, Camera cam)
        => new(
            Width, Height, tracables, cam, stride: Width * 4, lights: new[]
            {
                new Light { Position = new Vector3(0f, 6f, 5f), Color = Vector3.One },
            },
            renderOptions: new RenderOptions(Lighting: LightingMode.NEE, MaxSampleCount: 500),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(EnableDiffuseCache: false, SmokeMode: SmokeMode.None, SampleClamp: 0f),
            debugOptions: new DebugOptions());

    private static Camera Cam() => new()
    {
        Position = new Vector3(0f, 2.6f, -6f),
        Rotation = Quaternion.Identity,
        Fov = MathF.PI / 3f,
        Aspect = (float)Width / Height,
        ImgPlaneZ = 1f,
    };

    private static readonly Vector3[] LightPositions = [new Vector3(0f, 6f, 5f)];

    private static Vector3 PrimaryDir(Camera cam, int x, int y)
    {
        float tanHalfFov = MathF.Tan(cam.Fov * 0.5f);
        float aspectTanHalfFov = cam.Aspect * tanHalfFov;
        float px = (2f * ((x + 0.5f) / Width) - 1f) * aspectTanHalfFov;
        float py = (1f - 2f * ((y + 0.5f) / Height)) * tanHalfFov;
        return Vector3.Normalize(Vector3.Transform(new Vector3(px, py, cam.ImgPlaneZ), cam.Rotation));
    }

    private static Vector3 TraceOneSample(JobSystem js, Camera cam, int x, int y)
    {
        int ix = y * Width + x;
        js.AccumXYZ[ix] = Vector3.Zero;
        js.SampleCount[ix] = 0;
        js.Debug.DirectLightingXYZ[ix] = Vector3.Zero;
        js.Debug.IndirectLightingXYZ[ix] = Vector3.Zero;
        js.WavelengthCounter[ix] = 0;
        js.TraceCore(cam, y, x);
        return js.AccumXYZ[ix];
    }

    private static void AssertClose(Vector3 e, Vector3 a, string what)
    {
        for (int c = 0; c < 3; c++)
        {
            float ev = c == 0 ? e.X : c == 1 ? e.Y : e.Z;
            float av = c == 0 ? a.X : c == 1 ? a.Y : a.Z;
            float tol = 1e-3f + 1e-3f * MathF.Abs(ev);
            Assert.IsTrue(MathF.Abs(ev - av) <= tol, $"{what}[{c}]: expected {ev}, got {av}");
        }
    }

    [TestMethod]
    public void ShadeSample_MatchesTraceCore_Movers()
    {
        var cam = Cam();
        var (tracables, packedDyn) = BuildScene(dyn: true);
        var (_, packedStatic) = BuildScene(dyn: false);

        var wl = new WavelengthLookup();
        var res = SpectralResourceBaker.Bake(wl, packedDyn.Materials);
        var js = BuildGroundTruth(tracables, cam);
        var tracer = new BvhSceneTracer(tracables, res.DeterWavelengths[0]);

        int compared = 0, moverDiff = 0;
        for (int y = 3; y < Height; y += 4)
        {
            for (int x = 3; x < Width; x += 4)
            {
                Vector3 dir = PrimaryDir(cam, x, y);
                if (!tracer.ClosestHit(cam.Position, dir, out _, out _))
                    continue;

                Vector3 truth = TraceOneSample(js, cam, x, y);

                uint pixelHash = Phase1Reference.Hash2D(x, y);
                Vector3 refDyn = Phase2Reference.ShadeSample(
                    tracer, packedDyn.Primitives, res, LightPositions, LightingMode.NEE,
                    cam.Position, dir, pixelHash, 0u, out _, out _);
                // Same scene, movers left static — the full integrator. Where it differs, the cheap mover
                // branch actually changed the shading (proves it is exercised).
                Vector3 refStatic = Phase2Reference.ShadeSample(
                    tracer, packedStatic.Primitives, res, LightPositions, LightingMode.NEE,
                    cam.Position, dir, pixelHash, 0u, out _, out _);

                AssertClose(truth, refDyn, $"cpu==ref px=({x},{y})");
                compared++;
                if ((refDyn - refStatic).Length() > 5e-3f) moverDiff++;
            }
        }

        Assert.IsTrue(compared > 200, $"expected many surface comparisons, got {compared}");
        Assert.IsTrue(moverDiff > 0, "the cheap mover branch must change shading vs. the static path (branch exercised)");
    }
}
