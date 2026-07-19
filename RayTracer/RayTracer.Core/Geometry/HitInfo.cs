using System.Numerics;

namespace RayTracer;

/// <summary>
/// The result of a ray/primitive intersection. Replaces the former six-field
/// value tuple returned by <see cref="Tracable.Intersect"/> so the spectral and
/// optical effects can add channels without reshaping every call site
/// (spectral-effects-plan.md §0.1). The first six members are the original
/// geometry/shading fields; the optical members describe how the surface bends
/// and absorbs light and default to an opaque diffuse surface, so existing
/// diffuse primitives are unchanged.
/// </summary>
/// <param name="T">Ray parameter (distance) at the hit.</param>
/// <param name="Location">World-space hit point.</param>
/// <param name="Normal">World-space surface normal, oriented against the ray.</param>
/// <param name="Uv">Surface UV coordinates, when the primitive provides them.</param>
/// <param name="Reflectance">Spectral reflectance at the ray's wavelength (with any pattern attenuation baked in).</param>
/// <param name="Roughness">Surface roughness in [0,1].</param>
/// <param name="Surface">How the surface interacts with light; drives integrator branching.</param>
/// <param name="Transmission">Fraction of light transmitted (0 = opaque).</param>
/// <param name="Ior">Index of refraction resolved at the ray's wavelength (1 = vacuum/air).</param>
/// <param name="Extinction">Absorption coefficient σ for Beer–Lambert attenuation inside the medium (0 = none).</param>
/// <param name="Emission">Self-emitted spectral radiance at the ray's wavelength (0 = non-emitting). Added
/// at a hit so a <see cref="SurfaceKind.Emissive"/> surface glows and is carried into mirror/glass reflections
/// (pinball-plan §5.3).</param>
public readonly record struct HitInfo(
    float T,
    Vector3 Location,
    Vector3 Normal,
    Vector2? Uv,
    float Reflectance,
    float Roughness,
    SurfaceKind Surface = SurfaceKind.Diffuse,
    float Transmission = 0f,
    float Ior = 1f,
    float Extinction = 0f,
    float Emission = 0f)
{
    /// <summary>
    /// Builds a <see cref="HitInfo"/> for a confirmed hit on
    /// <paramref name="material"/>, resolving the optical channels from the
    /// material at <paramref name="wavelength"/>. The diffuse case (the vast
    /// majority of hits) takes a fast path that copies only the geometry/shading
    /// fields, leaving the optical members at their opaque-diffuse defaults so
    /// output is bit-identical to the pre-refactor tuple.
    /// </summary>
    public static HitInfo FromMaterial(
        float t,
        Vector3 location,
        Vector3 normal,
        Vector2? uv,
        float reflectance,
        MaterialData material,
        int wavelength,
        Vector3 rayDirection)
    {
        if (material.Surface == SurfaceKind.Diffuse)
            return new HitInfo(t, location, normal, uv, reflectance, material.Roughness);

        // Thin-film iridescence (§2.2) is a reflectance-only effect: modulate the base
        // reflectance by the interference factor at this wavelength and view angle, then
        // shade the surface as an ordinary (hero-only) diffuse hit. cosθ comes from the
        // ray and the unit face normal.
        if (material.Surface == SurfaceKind.ThinFilm)
        {
            float cosTheta = MathF.Abs(Vector3.Dot(Vector3.Normalize(rayDirection), normal));
            float thickness = Optics.FilmThicknessAt(
                material.FilmThicknessNm, material.FilmSpatialAmpNm, location.X, location.Z);
            float raw = Optics.ThinFilmReflectance(cosTheta, wavelength, thickness, material.IorAt(wavelength));
            reflectance *= Optics.ApplyFilmContrast(raw, material.FilmContrast);
            return new HitInfo(t, location, normal, uv, reflectance, material.Roughness,
                SurfaceKind.ThinFilm);
        }

        // Soap bubble (§2.6): a thin shell. The reflected component is tinted by the thin-film
        // interference at this wavelength + angle (constant shell thickness → angle alone gives
        // the rings); the specular chain applies the Fresnel reflect/transmit-straight split and
        // carries this tint on the reflection. Ior holds the film's index (for that Fresnel).
        if (material.Surface == SurfaceKind.Bubble)
        {
            // Thickness follows a gravity gradient over the sphere (thin at the top, thick at the
            // bottom), keyed on the surface normal's up-component, for the swirling colour bands.
            float cosTheta = MathF.Abs(Vector3.Dot(Vector3.Normalize(rayDirection), normal));
            float d = material.FilmThicknessNm * float.Lerp(1.4f, 0.6f, (normal.Y + 1f) * 0.5f);
            reflectance *= Optics.ThinFilmReflectance(cosTheta, wavelength, d, material.IorAt(wavelength));
            return new HitInfo(t, location, normal, uv, reflectance, material.Roughness,
                SurfaceKind.Bubble, material.Transmission, material.IorAt(wavelength), material.ExtinctionAt(wavelength));
        }

        return new HitInfo(
            t,
            location,
            normal,
            uv,
            reflectance,
            material.Roughness,
            material.Surface,
            material.Transmission,
            material.IorAt(wavelength),
            material.ExtinctionAt(wavelength),
            material.GetSpectralEmission(wavelength));
    }
}
