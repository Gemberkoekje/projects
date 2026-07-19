using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// GPU-port parity for Phase 2.2 thin-film iridescence. Thin-film is a reflectance-only
/// modulation whose reflection geometry is wavelength-independent, so — unlike glass — it
/// keeps the ordinary companion-wavelength diffuse path on both sides. This pins the pure-C#
/// GPU replica (<see cref="Phase2Reference.ShadeSample"/>, whose thin-film branch the HLSL
/// <c>PathTracePhase6.hlsl</c> ports) to the CPU renderer's <c>JobSystem.TraceCore</c> over a
/// scene whose primary rays strike an iridescent panel, so per sample the two must agree.
/// </summary>
[TestClass]
public sealed class GpuThinFilmTests
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

    // A flat-albedo iridescent film: neutral base, thickness d, film IOR in CauchyA.
    private static MaterialData Film(string id, float thicknessNm, float filmIor = 1.4f)
        => new(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            Spectrum(_ => 0.9f), SurfaceKind.ThinFilm, cauchyA: filmIor, filmThicknessNm: thicknessNm);

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

    private static Scene Build()
    {
        // A tilted iridescent panel so primary rays hit it across a spread of angles
        // (goniochromism), backed by nothing — the film's own albedo is what shows.
        var film = Film("film", thicknessNm: 320f);
        var scene = new Tracable[] { Wall(3f, film) };
        var lights = new[] { new Light { Position = new Vector3(1.0f, 1.5f, 0.5f), Color = Vector3.One } };

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
    public void ThinFilmShadeSample_MatchesTraceCore(LightingMode mode)
    {
        var s = Build();
        var js = BuildGroundTruth(s, mode);
        var tracer = new BvhSceneTracer(s.Tracables, s.Res.DeterWavelengths[0]);

        int compared = 0, filmHits = 0, coloured = 0;
        for (uint sampleIdx = 0; sampleIdx < 4; sampleIdx++)
        {
            for (int y = 4; y < Height; y += 6)
            {
                for (int x = 4; x < Width; x += 6)
                {
                    Vector3 dir = PrimaryDir(s.Camera, x, y);
                    if (!tracer.ClosestHit(s.Camera.Position, dir, out int quad, out _))
                        continue;
                    if (s.Packed.Primitives[quad].Surface == (uint)SurfaceKind.ThinFilm)
                        filmHits++;

                    var truth = TraceOneSample(js, s.Camera, x, y, sampleIdx);

                    uint pixelHash = Phase1Reference.Hash2D(x, y);
                    Vector3 refTotal = Phase2Reference.ShadeSample(
                        tracer, s.Packed.Primitives, s.Res, s.LightPositions, mode,
                        s.Camera.Position, dir, pixelHash, sampleIdx,
                        out Vector3 refDirect, out _, out _);

                    string at = $"px=({x},{y}) s={sampleIdx} mode={mode}";
                    AssertClose(truth.total, refTotal, $"total {at}");
                    AssertClose(truth.direct, refDirect, $"direct {at}");

                    compared++;
                    if (refTotal.Y > 1e-4f) coloured++;
                }
            }
        }

        Assert.IsTrue(compared > 50, $"expected many comparisons, got {compared}");
        Assert.IsTrue(filmHits > 50, $"primary rays should strike the film, got {filmHits}");
        Assert.IsTrue(coloured > 0, "the iridescent film should be non-black somewhere");
    }
}
