using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 2.3 of the spectral &amp; optical effects plan (spectral-effects-plan.md):
/// Beer–Lambert spectral absorption. Pins the physics of <see cref="Optics.AbsorptionAt"/>
/// and drives the CPU path tracer over a coloured-glass slab to check that light
/// travelling through it shifts hue and darkens with the path length — the continuous
/// hue-vs-thickness gradient that an RGB renderer can only approximate.
/// </summary>
[TestClass]
public sealed class AbsorptionTests
{
    private static SpectralData Spectrum(Func<int, float> reflectance)
    {
        var wl = new List<int>();
        var v = new List<float>();
        for (int w = 360; w <= 830; w += 5) { wl.Add(w); v.Add(reflectance(w)); }
        float[] a = v.ToArray();
        return new SpectralData(wl.ToArray(), a, (float[])a.Clone());
    }

    private static MaterialData Diffuse(string id, SpectralData spectrum)
        => new(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, spectrum, SurfaceKind.Diffuse);

    // Coloured glass: clear dielectric with a Beer–Lambert absorption (σ at R/G/B anchors).
    private static MaterialData Glass(string id, Vector3 absorptionRgb)
        => new(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            spectralData: null, surface: SurfaceKind.Dielectric, transmission: 0.95f,
            cauchyA: 1.5f, cauchyB: 0f, absorptionRgb: absorptionRgb);

    // A large quad at z = zPlane, normal facing −Z (toward a camera at the origin).
    private static TracableRectangle Wall(float zPlane, MaterialData material)
    {
        const float e = 20f;
        return new TracableRectangle(
            (new Vector3(-e, -e, zPlane), new Vector3(-e, e, zPlane), new Vector3(e, -e, zPlane)), material);
    }

    // A glass slab of the given thickness (front + back faces), so a ray travels a finite
    // distance inside the medium (the entry→exit segment is what absorbs).
    private static Tracable[] Slab(float frontZ, float thickness, MaterialData glass, MaterialData wall, float wallZ)
        => new Tracable[] { Wall(frontZ, glass), Wall(frontZ + thickness, glass), Wall(wallZ, wall) };

    private static Vector3 RenderCenter(Tracable[] scene, int frames = 3000, int size = 12)
    {
        var camera = new Camera
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 3f,
            Aspect = 1f,
            ImgPlaneZ = 1f,
        };
        var js = new JobSystem(
            size, size, scene, camera, stride: size * 4, lights: null,
            renderOptions: new RenderOptions(Lighting: LightingMode.None, MaxSampleCount: 20000),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(EnableTaa: false, EnableDiffuseCache: false, SmokeMode: SmokeMode.None),
            debugOptions: new DebugOptions());

        for (int f = 0; f < frames; f++)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    js.TraceCore(camera, y, x);

        return js.AccumXYZ[(size / 2) * size + (size / 2)];
    }

    private static (float x, float y) Chroma(Vector3 xyz)
    {
        float sum = xyz.X + xyz.Y + xyz.Z;
        return sum > 1e-8f ? (xyz.X / sum, xyz.Y / sum) : (0f, 0f);
    }

    [TestMethod]
    public void AbsorptionAt_InterpolatesRgbAnchors()
    {
        // σR=1, σG=2, σB=3 at 630/532/465; flat outside, linear between.
        Assert.AreEqual(3f, Optics.AbsorptionAt(1f, 2f, 3f, 400f), 1e-5f); // ≤465 → blue
        Assert.AreEqual(3f, Optics.AbsorptionAt(1f, 2f, 3f, 465f), 1e-5f);
        Assert.AreEqual(2f, Optics.AbsorptionAt(1f, 2f, 3f, 532f), 1e-5f); // green anchor
        Assert.AreEqual(1f, Optics.AbsorptionAt(1f, 2f, 3f, 630f), 1e-5f); // red anchor
        Assert.AreEqual(1f, Optics.AbsorptionAt(1f, 2f, 3f, 700f), 1e-5f); // ≥630 → red
        Assert.AreEqual(2.5f, Optics.AbsorptionAt(1f, 2f, 3f, 498.5f), 1e-3f); // halfway blue→green
    }

    [TestMethod]
    public void RedGlass_AbsorbsShortWavelengthsMore()
    {
        // Red glass: low σ at red, high at green/blue → it transmits red, absorbs the rest.
        var red = Glass("red", new Vector3(0.2f, 3f, 4f));
        Assert.IsTrue(red.ExtinctionAt(630) < red.ExtinctionAt(465),
            "Red glass must absorb blue more than red.");
        Assert.AreEqual(0f, Glass("clear", Vector3.Zero).ExtinctionAt(550), "Clear glass has no absorption.");
    }

    /// <summary>
    /// A red-glass slab in front of a white wall transmits a red-tinted image, and a thicker
    /// slab is darker and more saturated (Beer–Lambert: transmission falls exponentially with
    /// path length, faster at the strongly-absorbed wavelengths).
    /// </summary>
    [TestMethod]
    public void ColouredGlassSlab_ShiftsHueAndDarkensWithThickness()
    {
        var white = Diffuse("wall", Spectrum(_ => 0.9f));
        var red = Glass("red", new Vector3(0.3f, 4f, 5f));

        Vector3 direct = RenderCenter(new Tracable[] { Wall(3f, white) });
        Vector3 thin = RenderCenter(Slab(2f, 0.3f, red, white, 6f));
        Vector3 thick = RenderCenter(Slab(2f, 0.9f, red, white, 6f));

        var (dx, _) = Chroma(direct);
        var (tx, _) = Chroma(thin);
        var (cx, _) = Chroma(thick);

        Assert.IsTrue(thin.Y > 1e-4f, "The slab must transmit some light.");
        Assert.IsTrue(tx > dx + 0.02f, $"Red glass should push the transmitted colour toward red (wall {dx}, glass {tx}).");
        Assert.IsTrue(cx > tx + 0.01f, $"Thicker glass should saturate further toward red (thin {tx}, thick {cx}).");
        Assert.IsTrue(thick.Y < thin.Y, $"Thicker glass should transmit less light (thin Y {thin.Y}, thick Y {thick.Y}).");
    }
}
