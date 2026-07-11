using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 2.2 of the spectral &amp; optical effects plan (spectral-effects-plan.md):
/// thin-film interference / iridescence. Pins the physics of
/// <see cref="Optics.ThinFilmReflectance"/> (a per-wavelength phase computation that RGB
/// cannot express) and the end-to-end fold into a hit's reflectance for a
/// <see cref="SurfaceKind.ThinFilm"/> material — the reflectance-only modulation the CPU
/// path tracer and the GPU replica share.
/// </summary>
[TestClass]
public sealed class ThinFilmTests
{
    private static SpectralData FlatSpectrum(float value)
    {
        var wl = new List<int>();
        var v = new List<float>();
        for (int w = 360; w <= 830; w += 5) { wl.Add(w); v.Add(value); }
        float[] a = v.ToArray();
        return new SpectralData(wl.ToArray(), a, (float[])a.Clone());
    }

    [TestMethod]
    public void ThinFilmReflectance_ModulatesAcrossSpectrum()
    {
        // A ~300 nm film at normal incidence: reflectance oscillates with wavelength, so
        // the three primaries do not come back equal — that spread is the colour.
        const float n = 1.33f, d = 300f, cos = 1f;
        float rBlue = Optics.ThinFilmReflectance(cos, 450f, d, n);
        float rGreen = Optics.ThinFilmReflectance(cos, 550f, d, n);
        float rRed = Optics.ThinFilmReflectance(cos, 650f, d, n);

        foreach (float r in new[] { rBlue, rGreen, rRed })
            Assert.IsTrue(r is >= 0f and <= 1f, $"factor out of range: {r}");

        float spread = MathF.Max(rBlue, MathF.Max(rGreen, rRed)) - MathF.Min(rBlue, MathF.Min(rGreen, rRed));
        Assert.IsTrue(spread > 0.2f, $"Thin film should modulate reflectance across the spectrum (spread {spread}).");
    }

    [TestMethod]
    public void ThinFilmReflectance_ZeroThicknessIsNeutral()
        => Assert.AreEqual(1f, Optics.ThinFilmReflectance(1f, 550f, 0f, 1.4f), "No film → no modulation.");

    [TestMethod]
    public void ThinFilmReflectance_ThicknessSweepsHue()
    {
        // The wavelength of peak reflectance moves as the film thickens — the rainbow sweep.
        static int PeakWavelength(float d)
        {
            int best = 400;
            float bestR = -1f;
            for (int w = 400; w <= 700; w += 2)
            {
                float r = Optics.ThinFilmReflectance(1f, w, d, 1.4f);
                if (r > bestR) { bestR = r; best = w; }
            }
            return best;
        }

        Assert.AreNotEqual(PeakWavelength(210f), PeakWavelength(300f),
            "Peak reflectance wavelength should move as thickness changes.");
    }

    [TestMethod]
    public void ThinFilmReflectance_AngleShiftsColour()
    {
        // Goniochromism: the same film reflects a given wavelength differently at a steep
        // angle than head-on, because the in-film path length changes with cosθ_t.
        const float d = 300f, n = 1.4f, lambda = 550f;
        float rNormal = Optics.ThinFilmReflectance(1.0f, lambda, d, n);
        float rGrazing = Optics.ThinFilmReflectance(0.3f, lambda, d, n);
        Assert.IsTrue(MathF.Abs(rNormal - rGrazing) > 0.05f,
            $"Viewing angle should shift the colour (normal {rNormal}, grazing {rGrazing}).");
    }

    [TestMethod]
    public void ThinFilmMaterial_FoldsIridescenceIntoHitReflectance()
    {
        // A thin-film material with a flat 0.9 base albedo: a hit's reflectance is the base
        // modulated by the interference factor, so it differs by wavelength and never
        // exceeds the base — and the surface stays classified ThinFilm.
        var film = new MaterialData("film", "film", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            FlatSpectrum(0.9f), SurfaceKind.ThinFilm, cauchyA: 1.4f, filmThicknessNm: 300f);
        var quad = new TracableRectangle(
            (new Vector3(-1, -1, 2), new Vector3(1, -1, 2), new Vector3(-1, 1, 2)), film); // faces −Z

        HitInfo? blue = quad.Intersect(new Ray { Origin = Vector3.Zero, Direction = new Vector3(0, 0, 1), Wavelength = 450f, Intensity = 1f });
        HitInfo? red = quad.Intersect(new Ray { Origin = Vector3.Zero, Direction = new Vector3(0, 0, 1), Wavelength = 650f, Intensity = 1f });

        Assert.IsTrue(blue.HasValue && red.HasValue, "Ray should hit the film quad.");
        Assert.AreEqual(SurfaceKind.ThinFilm, blue.Value.Surface);
        Assert.IsTrue(blue.Value.Reflectance <= 0.9f + 1e-4f, "Modulated reflectance can't exceed the base albedo.");
        Assert.IsTrue(MathF.Abs(blue.Value.Reflectance - red.Value.Reflectance) > 0.05f,
            $"Iridescence: reflectance should differ by wavelength (blue {blue.Value.Reflectance}, red {red.Value.Reflectance}).");
    }
}
