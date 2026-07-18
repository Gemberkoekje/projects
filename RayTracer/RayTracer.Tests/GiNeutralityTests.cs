using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Pins realism finding §1 (the CIE² / CIE³ diffuse-GI restructure): multiply-bounced light must carry
/// <b>scalar</b> hero-λ throughput and pick up the CIE colour-matching curve exactly once, at the primary
/// surface. The historical code instead accumulated <c>XYZ×XYZ×XYZ</c> componentwise on the 2+-bounce
/// term (<c>baseXyz ⊙ secBaseXyz ⊙ tertBaseXyz</c>, each a single-hero-wavelength <c>CIE(λ)·ρ</c>).
///
/// Because the secondary and tertiary bounce rays share the primary's hero wavelength, the error is a
/// factor of <c>CIE(λ)²</c>. That does <b>not</b> cancel under the white balance: the white-balanced hero
/// set has <c>Σ CIE(λ)</c> neutral, but <c>Σ CIE(λ)²</c> is not (squaring re-weights each channel by its
/// own spread across the set), so the stray factor tints the 2+-bounce term even for a flat, neutral
/// reflectance. A closed neutral box driven by a neutral light therefore discriminates the fix from the
/// bug: the isolated 2+-bounce AOV — the exact buffer that carried <c>secBounce2Plus = secBaseXyz *
/// tertIncoming</c> — must resolve to a neutral grey (chromaticity ≈ the equal-energy point 1/3, 1/3).
///
/// (A single extra CIE factor would <i>not</i> discriminate — <c>Σ CIE</c> averages back to neutral — which
/// is also why the finding does not perturb the white point itself; see <see cref="SpectralColorTests"/>.)
/// </summary>
[TestClass]
public sealed class GiNeutralityTests
{
    private static SpectralData FlatSpectrum(float value, int min = 360, int max = 830, int step = 5)
    {
        var wavelengths = new List<int>();
        var values = new List<float>();
        for (int w = min; w <= max; w += step)
        {
            wavelengths.Add(w);
            values.Add(value);
        }
        float[] v = values.ToArray();
        return new SpectralData(wavelengths.ToArray(), v, (float[])v.Clone());
    }

    private static MaterialData NeutralDiffuse(float reflectance)
        => new("x", "X", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, FlatSpectrum(reflectance), SurfaceKind.Diffuse);

    // Six inward-facing walls of the cube [-e, e]³ (each edge1×edge2 points to the interior), so a diffuse
    // bounce in any direction lands on another neutral wall and the tertiary bounce is always populated.
    private static Tracable[] NeutralBox(float e, MaterialData mat)
    {
        TracableRectangle F(Vector3 l1, Vector3 l2, Vector3 l3) => new((l1, l2, l3), mat);
        return
        [
            F(new(-e, -e, e), new(-e, e, e), new(e, -e, e)),    // +Z, normal −Z
            F(new(-e, -e, -e), new(e, -e, -e), new(-e, e, -e)), // −Z, normal +Z
            F(new(e, -e, -e), new(e, -e, e), new(e, e, -e)),    // +X, normal −X
            F(new(-e, -e, -e), new(-e, e, -e), new(-e, -e, e)), // −X, normal +X
            F(new(-e, e, -e), new(e, e, -e), new(-e, e, e)),    // +Y, normal −Y
            F(new(-e, -e, -e), new(-e, -e, e), new(e, -e, -e)), // −Y, normal +Y
        ];
    }

    private static (float x, float y) Chroma(Vector3 xyz)
    {
        float sum = xyz.X + xyz.Y + xyz.Z;
        return sum > 1e-8f ? (xyz.X / sum, xyz.Y / sum) : (0f, 0f);
    }

    [TestMethod]
    public void ClosedNeutralBox_MultiBounceIndirectStaysNeutral()
    {
        const int size = 16;
        const int frames = 400;
        var scene = NeutralBox(10f, NeutralDiffuse(0.6f));

        var camera = new Camera
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity, // looks down +Z
            Fov = MathF.PI / 3f,
            Aspect = 1f,
            ImgPlaneZ = 1f,
        };

        // A single flat-white (temp-0 → neutral spectrum) light inside the box drives NEE at every bounce;
        // with the ambient fill it populates the whole chain (secBounce2Plus = secReflHero · tertReflHero ·
        // (Ambient + neutral direct)). Every contribution is a pure product of neutral reflectances and a
        // neutral illuminant through the spectral integrator — neutral in must give neutral out.
        Light[] lights = [new Light { Position = new Vector3(0f, 8f, 0f), Color = Vector3.One }];

        var js = new JobSystem(
            size, size, scene, camera, stride: size * 4, lights: lights,
            renderOptions: new RenderOptions(Lighting: LightingMode.NEE, MaxSampleCount: frames + 10),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(
                EnableTaa: false, EnableDiffuseCache: false, SmokeMode: SmokeMode.None),
            debugOptions: new DebugOptions());

        for (int f = 0; f < frames; f++)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    js.TraceCore(camera, y, x);

        // Aggregate the isolated 2+-bounce AOV over the whole frame (averages out per-pixel MC chroma
        // noise). This is the buffer the CIE³ error wrote to.
        Vector3 bounce2Plus = Vector3.Zero;
        Vector3 total = Vector3.Zero;
        for (int i = 0; i < size * size; i++)
        {
            bounce2Plus += js._bounce2PlusXYZ[i];
            total += js.AccumXYZ[i];
        }

        Assert.IsTrue(bounce2Plus.Y > 1e-3f,
            $"the closed box must produce a real 2+-bounce term to test (Y={bounce2Plus.Y})");

        // The equal-energy white point is X=Y=Z → chromaticity (1/3, 1/3). The CIE² regression skews the
        // 2+-bounce term by a Σ CIE² tint (order 0.05+ in chroma); the scalar-throughput fix holds it to
        // MC noise.
        var (bx, by) = Chroma(bounce2Plus);
        Assert.AreEqual(1f / 3f, bx, 0.02f, $"2+-bounce chroma x drifted from neutral (xyz={bounce2Plus})");
        Assert.AreEqual(1f / 3f, by, 0.02f, $"2+-bounce chroma y drifted from neutral (xyz={bounce2Plus})");

        // And the composited frame stays neutral in sRGB.
        var rgb = JobSystem.ToSRGB(total / (size * size));
        Assert.IsTrue(MathF.Abs(rgb.X - rgb.Y) < 0.03f * MathF.Max(rgb.Y, 1e-3f) + 5e-3f,
            $"frame R−G not neutral (rgb={rgb})");
        Assert.IsTrue(MathF.Abs(rgb.Y - rgb.Z) < 0.03f * MathF.Max(rgb.Y, 1e-3f) + 5e-3f,
            $"frame G−B not neutral (rgb={rgb})");
    }
}
