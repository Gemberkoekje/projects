namespace RayTracer;

/// <summary>
/// Classifies how a surface interacts with light so the path integrator can
/// branch between ordinary diffuse shading and the wavelength-dependent optical
/// effects (dispersion, thin-film interference, fluorescence, …). Every
/// <see cref="MaterialData"/> carries one of these; measured maze materials are
/// <see cref="Diffuse"/> by default.
/// </summary>
public enum SurfaceKind
{
    /// <summary>Lambertian diffuse reflectance — the default for measured materials.</summary>
    Diffuse,

    /// <summary>Specular reflector; optionally roughened for glossy/metallic surfaces.</summary>
    Mirror,

    /// <summary>Transparent dielectric (glass, water) that both reflects and refracts.</summary>
    Dielectric,

    /// <summary>Thin-film interference (soap bubble, oil slick, iridescent shell).</summary>
    ThinFilm,

    /// <summary>Diffraction grating (CD/DVD rainbow).</summary>
    Grating,

    /// <summary>Absorbs a short wavelength and re-emits at a longer one.</summary>
    Fluorescent,

    /// <summary>Self-emitting surface (area/emissive light).</summary>
    Emissive,
}
