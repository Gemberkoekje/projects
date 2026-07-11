using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// GPU-port parity for Phase 1.2 clear dielectric (glass). Pins the pure-C# GPU
/// replica (<see cref="Phase2Reference.ShadeSample"/>, whose dielectric branch the
/// HLSL <c>PathTracePhase*.hlsl</c> shaders port) to the CPU renderer's own
/// <c>JobSystem.TraceCore</c> over a scene whose primary rays strike a glass pane in
/// front of a lit wall. The Fresnel reflect/refract roulette is deterministic given
/// the pixel/sample seed, so per-sample the two must agree.
/// </summary>
[TestClass]
public sealed class GpuGlassTests
{
    private const int Width = 64;
    private const int Height = 48;

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

    private static MaterialData Diffuse(string id, SpectralData spectrum)
        => new(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, spectrum, SurfaceKind.Diffuse);

    private static MaterialData Glass(string id, float ior, float cauchyB = 0f)
        => new(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            spectralData: null, surface: SurfaceKind.Dielectric, transmission: 0.95f, cauchyA: ior, cauchyB: cauchyB);

    private static TracableRectangle Wall(float zPlane, MaterialData material)
    {
        const float e = 20f;
        Vector3 l1 = new(-e, -e, zPlane);
        Vector3 l2 = new(-e, e, zPlane);   // +Y
        Vector3 l3 = new(e, -e, zPlane);   // +X → normal −Z (faces the camera)
        return new TracableRectangle((l1, l2, l3), material);
    }

    private sealed record Scene(
        Tracable[] Tracables, PackedScene Packed, SpectralResources Res,
        Light[] Lights, Vector3[] LightPositions, Camera Camera);

    private static Scene Build(float cauchyB = 0f)
    {
        var glass = Glass("glass", ior: 1.5f, cauchyB: cauchyB);
        var wall = Diffuse("wall", Spectrum(w => 0.05f + 0.80f * Math.Clamp((w - 500f) / 150f, 0f, 1f)));

        var scene = new Tracable[] { Wall(2f, glass), Wall(6f, wall) };
        var lights = new[] { new Light { Position = new Vector3(1.5f, 1.0f, 4.5f), Color = Vector3.One } };

        var wl = new WavelengthLookup();
        var packed = GpuScenePacker.Pack(scene);
        var res = SpectralResourceBaker.Bake(wl, packed.Materials);
        var packedLights = LightPacker.Pack(lights);

        var camera = new Camera
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
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

    private static (Vector3 total, Vector3 direct) TraceOneSample(JobSystem js, Camera cam, int x, int y, uint sampleIdx)
    {
        int ix = y * Width + x;
        js.AccumXYZ[ix] = Vector3.Zero;
        js.SampleCount[ix] = 0;
        js.Debug.DirectLightingXYZ[ix] = Vector3.Zero;
        js.Debug.IndirectLightingXYZ[ix] = Vector3.Zero;
        js.WavelengthCounter[ix] = sampleIdx;
        js.TraceCore(cam, y, x);
        return (js.AccumXYZ[ix], js.Debug.DirectLightingXYZ[ix]);
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

    [TestMethod]
    [DataRow(LightingMode.NEE, DisplayName = "NEE")]
    [DataRow(LightingMode.None, DisplayName = "None")]
    public void GlassShadeSample_MatchesTraceCore(LightingMode mode) => RunParity(Build(), mode);

    /// <summary>
    /// Dispersion parity (spectral-effects-plan §2.1): a strongly dispersive glass
    /// (<c>CauchyB &gt; 0</c>) refracts each hero wavelength by a different angle, so the
    /// refracted ray lands on a different part of the wall per wavelength. Both the CPU
    /// renderer and the GPU replica resolve the IOR from the same hero wavelength
    /// (<c>MaterialData.IorAt</c> vs <c>Phase2Reference.IorAtHero</c>), so they must still
    /// agree per sample — this pins that the reference's wavelength-dependent IOR matches.
    /// </summary>
    [TestMethod]
    [DataRow(LightingMode.NEE, DisplayName = "NEE")]
    [DataRow(LightingMode.None, DisplayName = "None")]
    public void DispersiveGlassShadeSample_MatchesTraceCore(LightingMode mode) => RunParity(Build(cauchyB: 0.02f), mode);

    private static void RunParity(Scene s, LightingMode mode)
    {
        var js = BuildGroundTruth(s, mode);
        var tracer = new BvhSceneTracer(s.Tracables, s.Res.DeterWavelengths[0]);

        int compared = 0, glassHits = 0, lit = 0;
        for (uint sampleIdx = 0; sampleIdx < 4; sampleIdx++)
        {
            for (int y = 4; y < Height; y += 7)
            {
                for (int x = 4; x < Width; x += 7)
                {
                    Vector3 dir = PrimaryDir(s.Camera, x, y);
                    if (!tracer.ClosestHit(s.Camera.Position, dir, out int quad, out _))
                        continue;
                    if (s.Packed.Primitives[quad].Surface == (uint)SurfaceKind.Dielectric)
                        glassHits++;

                    var truth = TraceOneSample(js, s.Camera, x, y, sampleIdx);

                    uint pixelHash = Phase1Reference.Hash2D(x, y);
                    Vector3 refTotal = Phase2Reference.ShadeSample(
                        tracer, s.Packed.Primitives, s.Res, s.LightPositions, mode,
                        s.Camera.Position, dir, pixelHash, sampleIdx,
                        out Vector3 refDirect, out _);

                    string at = $"px=({x},{y}) s={sampleIdx} mode={mode}";
                    AssertClose(truth.total, refTotal, $"total {at}");
                    AssertClose(truth.direct, refDirect, $"direct {at}");

                    compared++;
                    if (refTotal.Y > 1e-4f) lit++;
                }
            }
        }

        Assert.IsTrue(compared > 50, $"expected many comparisons, got {compared}");
        Assert.IsTrue(glassHits > 50, $"primary rays should strike the glass, got {glassHits}");
        Assert.IsTrue(lit > 0, "glass transmitting the lit wall should be non-black somewhere");
    }
}
