using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 1.2 of the spectral &amp; optical effects plan (spectral-effects-plan.md):
/// clear dielectric (glass). Drives the CPU path tracer over explicit glass/wall
/// scenes and checks that a clear glass pane in front of a wall transmits the wall
/// (you see through it), tinted only by the Fresnel loss, and that dielectrics are
/// classified as specular + wavelength-dependent. Fresnel/refraction physics are
/// pinned separately in <see cref="SpectralFoundationTests"/>.
/// </summary>
[TestClass]
public sealed class GlassRefractionTests
{
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
            spectralData: null, surface: SurfaceKind.Dielectric,
            transmission: 0.95f, cauchyA: ior, cauchyB: cauchyB);

    // A large quad at z = zPlane, geometric normal facing −Z (toward a camera at the origin).
    private static TracableRectangle Wall(float zPlane, MaterialData material)
    {
        const float e = 20f;
        Vector3 l1 = new(-e, -e, zPlane);
        Vector3 l2 = new(-e, e, zPlane);   // +Y edge
        Vector3 l3 = new(e, -e, zPlane);   // +X edge → cross(+Y,+X) = −Z
        return new TracableRectangle((l1, l2, l3), material);
    }

    private static Vector3 RenderCenter(Tracable[] scene, int frames = 256, int size = 16)
    {
        var camera = new Camera
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity, // looks down +Z
            Fov = MathF.PI / 3f,
            Aspect = 1f,
            ImgPlaneZ = 1f,
        };

        var js = new JobSystem(
            size, size, scene, camera, stride: size * 4, lights: null,
            renderOptions: new RenderOptions(Lighting: LightingMode.None, MaxSampleCount: 4000),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(
                EnableTaa: false, EnableDiffuseCache: false, SmokeMode: SmokeMode.None),
            debugOptions: new DebugOptions());

        for (int f = 0; f < frames; f++)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    js.TraceCore(camera, y, x);

        int center = (size / 2) * size + (size / 2);
        return js.AccumXYZ[center];
    }

    private static (float x, float y) Chroma(Vector3 xyz)
    {
        float sum = xyz.X + xyz.Y + xyz.Z;
        return sum > 1e-8f ? (xyz.X / sum, xyz.Y / sum) : (0f, 0f);
    }

    [TestMethod]
    public void Dielectric_IsSpecularAndWavelengthDependent()
    {
        Assert.IsTrue(Optics.IsSpecular(SurfaceKind.Dielectric));
        Assert.IsTrue(Optics.IsWavelengthDependent(SurfaceKind.Dielectric));
    }

    /// <summary>
    /// Dispersion (spectral-effects-plan §2.1): a glass with a positive Cauchy B
    /// coefficient has a higher index of refraction toward the blue end, so blue light
    /// bends more than red at the same interface — the two wavelengths fan apart. This
    /// pins the exact chain the render path uses: the IOR is resolved from the ray's
    /// wavelength (<see cref="MaterialData.IorAt"/>) and the ray is bent with
    /// <see cref="Optics.Refract"/> — the specular integrator does nothing more.
    /// </summary>
    [TestMethod]
    public void DispersiveGlass_RefractsBlueMoreThanRed()
    {
        var glass = Glass("prism", ior: 1.5f, cauchyB: 0.01f);
        float nBlue = glass.IorAt(430f);
        float nRed = glass.IorAt(680f);
        Assert.IsTrue(nBlue > nRed + 1e-3f, $"Blue should see a higher IOR than red (blue {nBlue}, red {nRed}).");

        // Oblique ray crossing air → glass.
        Vector3 incident = Vector3.Normalize(new Vector3(MathF.Sin(0.7f), 0f, MathF.Cos(0.7f)));
        Vector3 normal = new(0f, 0f, -1f); // surface faces the incoming ray

        Assert.IsTrue(Optics.Refract(incident, normal, 1f, nBlue, out Vector3 rBlue));
        Assert.IsTrue(Optics.Refract(incident, normal, 1f, nRed, out Vector3 rRed));

        // Higher-IOR blue bends toward the normal more, so it deviates from the incident
        // direction further than red does.
        float devBlue = MathF.Acos(Math.Clamp(Vector3.Dot(rBlue, incident), -1f, 1f));
        float devRed = MathF.Acos(Math.Clamp(Vector3.Dot(rRed, incident), -1f, 1f));
        Assert.IsTrue(devBlue > devRed + 1e-3f, $"Blue must bend more than red (devBlue {devBlue}, devRed {devRed}).");

        float spread = MathF.Acos(Math.Clamp(Vector3.Dot(rBlue, rRed), -1f, 1f));
        Assert.IsTrue(spread > 1e-3f, $"Blue and red should fan into different directions (spread {spread} rad).");
    }

    /// <summary>
    /// A clear glass pane in front of a coloured wall transmits the wall at normal
    /// incidence: the glass pixel takes on the wall's hue, dimmed only slightly by the
    /// Fresnel reflection (~4% at n=1.5). You see through the glass.
    /// </summary>
    [TestMethod]
    public void ClearGlass_TransmitsWallAtNormalIncidence()
    {
        var redWall = Diffuse("wall", Spectrum(w => 0.05f + 0.80f * Math.Clamp((w - 500f) / 150f, 0f, 1f)));
        var glass = Glass("glass", ior: 1.5f);

        Vector3 throughGlass = RenderCenter(new Tracable[]
        {
            Wall(2f, glass),   // glass pane in front
            Wall(5f, redWall), // wall behind it
        });
        Vector3 direct = RenderCenter(new Tracable[] { Wall(2f, redWall) });

        Assert.IsTrue(throughGlass.Y > 1e-3f, "Clear glass in front of a lit wall must not be black.");

        var (gx, gy) = Chroma(throughGlass);
        var (dx, dy) = Chroma(direct);
        Assert.AreEqual(dx, gx, 8e-3f, "Transmitted hue (x) should match the wall seen directly.");
        Assert.AreEqual(dy, gy, 8e-3f, "Transmitted hue (y) should match the wall seen directly.");

        // At n=1.5 normal incidence Fresnel reflects ~4%, so ~96% transmits.
        float ratio = throughGlass.Y / direct.Y;
        Assert.IsTrue(ratio is > 0.88f and < 1.0f,
            $"Clear glass should transmit ~96% of the wall's brightness (got {ratio:F3}).");
    }
}
