using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 2.6 of the spectral &amp; optical effects plan: soap bubbles. A bubble is a thin shell
/// (§0.4 sphere) that transmits straight through but carries a thin-film-tinted reflection whose
/// rings vary with view angle and with a gravity thickness gradient over the sphere. These tests
/// pin the surface classification and the reflectance folding done in <see cref="HitInfo.FromMaterial"/>.
/// </summary>
[TestClass]
public sealed class BubbleTests
{
    private const float FilmIor = 1.33f;
    private const float ThicknessNm = 500f;

    private static MaterialData Bubble()
        => new("bubble", "Bubble", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            FlatSpectrum(1.0f), SurfaceKind.Bubble, cauchyA: FilmIor, cauchyB: 0f, filmThicknessNm: ThicknessNm);

    private static SpectralData FlatSpectrum(float value)
    {
        var wl = new List<int>();
        var v = new List<float>();
        for (int w = 360; w <= 830; w += 5) { wl.Add(w); v.Add(value); }
        float[] a = v.ToArray();
        return new SpectralData(wl.ToArray(), a, (float[])a.Clone());
    }

    [TestMethod]
    public void Bubble_IsSpecularAndWavelengthDependent()
    {
        Assert.IsTrue(Optics.IsSpecular(SurfaceKind.Bubble), "reflect/transmit split runs in the specular chain");
        Assert.IsTrue(Optics.IsWavelengthDependent(SurfaceKind.Bubble), "thin-film tint needs hero-only sampling");
    }

    [TestMethod]
    public void FromMaterial_FoldsThinFilmTint_AndCarriesFilmIor()
    {
        // Front-face hit: normal −Z, ray heading +Z, so cosθ = 1 and the gravity gradient is neutral
        // (normal.Y = 0 → d = thickness × lerp(1.4, 0.6, 0.5) = thickness × 1.0).
        var mat = Bubble();
        var normal = new Vector3(0, 0, -1);
        var dir = new Vector3(0, 0, 1);
        const int wavelength = 550;

        HitInfo h = HitInfo.FromMaterial(4f, new Vector3(0, 1.5f, 4f), normal, null, 1.0f, mat, wavelength, dir);

        float expectedFilm = Optics.ThinFilmReflectance(1f, wavelength, ThicknessNm, mat.IorAt(wavelength));
        Assert.AreEqual(SurfaceKind.Bubble, h.Surface);
        Assert.AreEqual(FilmIor, h.Ior, 1e-4f, "Ior carries the film index for the reflection's Fresnel");
        Assert.AreEqual(expectedFilm, h.Reflectance, 1e-4f, "base reflectance 1.0 is modulated by the film factor");
    }

    [TestMethod]
    public void FromMaterial_GravityGradient_TintsTopAndBottomDifferently()
    {
        // Same wavelength and viewing angle (cosθ = 1) top vs bottom of the shell: the gravity
        // thickness gradient (thin near the top, thick near the bottom) must change the ring colour.
        var mat = Bubble();
        const int wavelength = 550;

        HitInfo top = HitInfo.FromMaterial(1f, Vector3.Zero, new Vector3(0, 1, 0), null, 1.0f, mat, wavelength, new Vector3(0, -1, 0));
        HitInfo bottom = HitInfo.FromMaterial(1f, Vector3.Zero, new Vector3(0, -1, 0), null, 1.0f, mat, wavelength, new Vector3(0, 1, 0));

        Assert.AreNotEqual(top.Reflectance, bottom.Reflectance, "gravity gradient produces different tints across the shell");
    }
}
