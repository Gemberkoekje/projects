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
    /// longer share the hero ray's geometry.
    /// </summary>
    public static bool IsWavelengthDependent(SurfaceKind kind)
        => kind is SurfaceKind.Dielectric
            or SurfaceKind.ThinFilm
            or SurfaceKind.Grating
            or SurfaceKind.Fluorescent;

    /// <summary>
    /// True for surfaces shaded by tracing a specular chain (mirror reflection or
    /// dielectric reflect/refract) rather than by diffuse sampling. These share the
    /// specular-ray plumbing (spectral-effects-plan.md §1.1–§1.2).
    /// </summary>
    public static bool IsSpecular(SurfaceKind kind)
        => kind is SurfaceKind.Mirror or SurfaceKind.Dielectric;

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
