using System.Numerics;

namespace RayTracer;

/// <summary>
/// Wavelength-agnostic geometric-optics helpers shared by the dielectric,
/// prism, and water effects (spectral-effects-plan.md §0.2). The routines
/// themselves know nothing about wavelength — dispersion enters purely through
/// the caller's choice of <c>iorTo = MaterialData.IorAt(ray.Wavelength)</c>.
/// </summary>
public static class Optics
{
    /// <summary>
    /// True for the surface kinds whose light path or emitted wavelength depends
    /// on the ray's wavelength. When a path touches one of these the integrator
    /// must fall back to hero-only single-wavelength sampling
    /// (spectral-effects-plan.md §0.3), since companion wavelengths would no
    /// longer share the hero ray's geometry. Thin-film is deliberately excluded:
    /// it is a reflectance-only modulation (§2.2) whose reflection geometry is
    /// wavelength-independent, so companion wavelengths validly share the path and
    /// each just re-evaluates the interference factor at its own λ.
    /// </summary>
    public static bool IsWavelengthDependent(SurfaceKind kind)
        => kind is SurfaceKind.Dielectric
            or SurfaceKind.Grating
            or SurfaceKind.Fluorescent
            or SurfaceKind.Bubble;   // thin-film-tinted reflection → build the spectrum hero-only (§2.6)

    /// <summary>
    /// True for surfaces shaded by tracing a specular chain (mirror reflection or
    /// dielectric reflect/refract) rather than by diffuse sampling. These share the
    /// specular-ray plumbing (spectral-effects-plan.md §1.1–§1.2).
    /// </summary>
    public static bool IsSpecular(SurfaceKind kind)
        => kind is SurfaceKind.Mirror or SurfaceKind.Dielectric or SurfaceKind.Bubble;

    /// <summary>
    /// Beer–Lambert absorption coefficient σ (per world unit) at a wavelength, interpolated
    /// from three anchors — red (630 nm), green (532 nm), blue (465 nm) — piecewise-linearly
    /// (spectral-effects-plan §2.3). This is how a coloured glass/liquid gets its hue: it
    /// absorbs the complement of its colour, so light travelling through it shifts hue and
    /// darkens with distance via <c>exp(−σ(λ)·d)</c>. Shared by the CPU, the GPU reference,
    /// and the HLSL shader so the tint matches.
    /// </summary>
    /// <summary>
    /// Effective glass path length (world units) a shadow ray accrues per dielectric interface it
    /// crosses, giving coloured (stained) glass a Beer–Lambert-tinted shadow — <c>exp(−σ(λ)·d)</c> —
    /// rather than a merely Fresnel-dimmed one (shadows-and-caustics-plan §A). A thin architectural
    /// pane is two dielectric quads, so a full pane contributes ≈ 2× this, matching the forward
    /// render's absorption over the pane's in-glass segment so the shadow's hue tracks the pane's
    /// transmitted colour. Clear glass (σ = 0) yields <c>exp(0) = 1</c>, so clear-only and glass-free
    /// scenes stay byte-identical. The HLSL twin is <c>GLASS_SHADOW_INTERFACE_THICKNESS</c>.
    /// </summary>
    public const float GlassShadowInterfaceThickness = 0.03f;

    public static float AbsorptionAt(float sigmaRed, float sigmaGreen, float sigmaBlue, float wavelengthNm)
    {
        const float blue = 465f, green = 532f, red = 630f;
        if (wavelengthNm <= blue) return sigmaBlue;
        if (wavelengthNm <= green) return float.Lerp(sigmaBlue, sigmaGreen, (wavelengthNm - blue) / (green - blue));
        if (wavelengthNm <= red) return float.Lerp(sigmaGreen, sigmaRed, (wavelengthNm - green) / (red - green));
        return sigmaRed;
    }

    /// <summary>
    /// Refracts <paramref name="incident"/> (pointing toward the surface) across
    /// a boundary from index <paramref name="iorFrom"/> into
    /// <paramref name="iorTo"/> using Snell's law. The normal may face either
    /// side; it is flipped internally to oppose the incident ray. Returns
    /// <c>false</c> on total internal reflection, in which case
    /// <paramref name="refracted"/> is set to the mirror reflection so callers
    /// can keep tracing.
    /// </summary>
    public static bool Refract(Vector3 incident, Vector3 normal, float iorFrom, float iorTo, out Vector3 refracted)
    {
        Vector3 i = Vector3.Normalize(incident);
        Vector3 n = normal;
        float cosI = Vector3.Dot(i, n);

        // Orient the normal to point against the incident ray so cosI >= 0.
        if (cosI > 0f)
            n = -n;
        else
            cosI = -cosI;

        float eta = iorFrom / iorTo;
        float k = 1f - eta * eta * (1f - cosI * cosI);
        if (k < 0f)
        {
            // Total internal reflection — no transmitted ray exists.
            refracted = Vector3.Reflect(i, n);
            return false;
        }

        refracted = Vector3.Normalize(eta * i + (eta * cosI - MathF.Sqrt(k)) * n);
        return true;
    }

    /// <summary>
    /// Multiplier on a bubble's Fresnel reflectance (spectral-effects-plan.md §2.6) so the
    /// thin-film rings read across the whole shell, not just at the grazing rim, while the
    /// bubble stays mostly see-through. Shared by the specular chain and the shadow-ray
    /// transmittance so a bubble's reflected tint and its cast shadow use the same split.
    /// Mirrors the HLSL <c>BUBBLE_REFLECT_BOOST</c>.
    /// </summary>
    public const float BubbleReflectBoost = 45f;

    /// <summary>
    /// Probability that a ray reflects off (rather than transmits straight through) a
    /// thin-shell soap bubble at one wavelength: the boosted air→film Fresnel reflectance
    /// times the thin-film-tinted base reflectance, clamped to [0,1]
    /// (spectral-effects-plan.md §2.6). The transmitted fraction is <c>1 − this</c>, which is
    /// what a shadow ray keeps when it passes through a bubble (a faint, tinted shadow rather
    /// than a hard black disc). <paramref name="tintedReflectance"/> is the base reflectance
    /// already modulated by <see cref="ThinFilmReflectance"/> (as folded in
    /// <c>HitInfo.FromMaterial</c>); <paramref name="filmIor"/> is the film's index.
    /// </summary>
    public static float BubbleReflectProbability(float cosTheta, float filmIor, float tintedReflectance, float boost = BubbleReflectBoost)
        => Math.Clamp(boost * FresnelDielectric(cosTheta, 1f, filmIor), 0f, 1f) * tintedReflectance;

    /// <summary>
    /// Exact (unpolarized) Fresnel reflectance at a dielectric boundary.
    /// <paramref name="cosThetaI"/> is the cosine of the incidence angle in the
    /// <paramref name="iorFrom"/> medium (use its absolute value). Returns a
    /// reflectance in [0,1]; grazing incidence and total internal reflection
    /// both return 1. This is the accurate reference used by the glass/water
    /// effects; <see cref="FresnelSchlick"/> is the cheaper approximation.
    /// </summary>
    public static float FresnelDielectric(float cosThetaI, float iorFrom, float iorTo)
    {
        cosThetaI = Math.Clamp(MathF.Abs(cosThetaI), 0f, 1f);
        float sinThetaI = MathF.Sqrt(MathF.Max(0f, 1f - cosThetaI * cosThetaI));
        float sinThetaT = iorFrom / iorTo * sinThetaI;

        // Total internal reflection.
        if (sinThetaT >= 1f)
            return 1f;

        float cosThetaT = MathF.Sqrt(MathF.Max(0f, 1f - sinThetaT * sinThetaT));

        float rs = (iorFrom * cosThetaI - iorTo * cosThetaT) / (iorFrom * cosThetaI + iorTo * cosThetaT);
        float rp = (iorFrom * cosThetaT - iorTo * cosThetaI) / (iorFrom * cosThetaT + iorTo * cosThetaI);
        return 0.5f * (rs * rs + rp * rp);
    }

    /// <summary>
    /// Two-beam thin-film interference reflectance factor in [0,1] at one wavelength
    /// (spectral-effects-plan.md §2.2). A wave reflected off the top of a film of index
    /// <paramref name="filmIor"/> and thickness <paramref name="filmThicknessNm"/>
    /// interferes with the wave that travels through and reflects off the bottom; the two
    /// are in phase (bright) where <c>2·n·d·cosθ_t</c> is an odd multiple of <c>λ/2</c>.
    /// The result modulates a surface's base reflectance, so the reflected colour sweeps
    /// the rainbow as the thickness or the view angle changes (goniochromism). Returns 1
    /// (no modulation) for a zero-thickness film. <paramref name="cosThetaI"/> is the
    /// cosine of the incidence angle in air; the angle inside the film follows from Snell.
    /// </summary>
    public static float ThinFilmReflectance(float cosThetaI, float wavelengthNm, float filmThicknessNm, float filmIor)
    {
        if (filmThicknessNm <= 0f) return 1f;

        cosThetaI = Math.Clamp(MathF.Abs(cosThetaI), 0f, 1f);
        float sinI2 = 1f - cosThetaI * cosThetaI;
        float cosThetaT = MathF.Sqrt(MathF.Max(0f, 1f - sinI2 / (filmIor * filmIor)));

        // Half the round-trip phase: δ/2 = 2π·n·d·cosθ_t / λ. Reflectance ∝ sin²(δ/2)
        // (equal-amplitude two-beam interference with the π shift at the first interface).
        float halfPhase = 2f * MathF.PI * filmIor * filmThicknessNm * cosThetaT / wavelengthNm;
        float s = MathF.Sin(halfPhase);
        return s * s;
    }

    /// <summary>
    /// A smooth, world-position keyed swirl in roughly [-1,1] used to vary thin-film
    /// thickness across a surface (the mottled bands of an oil slick, §2.2). A fixed sum
    /// of sines so the CPU, the GPU reference, and the HLSL shader all agree bit-for-bit.
    /// </summary>
    public static float FilmSwirl(float x, float z)
        => 0.55f * MathF.Sin(4.1f * x + 3.3f * z)
         + 0.30f * MathF.Sin(2.7f * x - 5.2f * z + 1.3f)
         + 0.15f * MathF.Sin(6.9f * x + 1.7f * z + 2.9f);

    /// <summary>
    /// Effective thin-film thickness at a world position: the base thickness plus the
    /// spatial swirl scaled by <paramref name="ampNm"/>, clamped to a positive value.
    /// Shared by every renderer so the interference pattern matches.
    /// </summary>
    public static float FilmThicknessAt(float baseNm, float ampNm, float x, float z)
        => MathF.Max(1f, baseNm + ampNm * FilmSwirl(x, z));

    /// <summary>
    /// Desaturates a thin-film reflectance factor by pulling it toward its mean (½):
    /// <c>½ + contrast·(raw − ½)</c>, clamped to [0,1]. <paramref name="contrast"/> = 1
    /// leaves it untouched; lower values give a pastel sheen (§2.2).
    /// </summary>
    public static float ApplyFilmContrast(float raw, float contrast)
        => Math.Clamp(0.5f + contrast * (raw - 0.5f), 0f, 1f);

    /// <summary>
    /// Schlick's approximation to <see cref="FresnelDielectric"/>. Cheaper (no
    /// transmitted-angle solve) and adequate for the initial glass/water passes;
    /// diverges from the exact curve only near grazing angles.
    /// </summary>
    public static float FresnelSchlick(float cosThetaI, float iorFrom, float iorTo)
    {
        float r0 = (iorFrom - iorTo) / (iorFrom + iorTo);
        r0 *= r0;
        cosThetaI = Math.Clamp(MathF.Abs(cosThetaI), 0f, 1f);
        float m = 1f - cosThetaI;
        float m2 = m * m;
        return r0 + (1f - r0) * m2 * m2 * m;
    }
}
