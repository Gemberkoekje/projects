namespace RayTracer;

/// <summary>
/// Quality tier used to map user-facing caustic controls to a concrete photon budget
/// (shadows-and-caustics-plan §B5). Mirrors <see cref="VolumetricQuality"/>.
/// </summary>
public enum CausticQuality
{
    None = 0,
    Off = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Ultra = 4,
}

/// <summary>
/// Controls the forward caustic transport (shadows-and-caustics-plan §B): a spectral photon map is
/// built from the lights toward the specular casters (glass/jewels/bubbles) and its density estimate
/// is added at each diffuse hit, so a prism throws a wavelength-ordered rainbow and a glass sphere a
/// bright focus onto the floor — light paths a backward path tracer with point-light NEE cannot form.
/// The default is disabled, so with no casters (or caustics off) renders are byte-identical.
/// </summary>
/// <param name="Enable">Master switch. When off the photon map is never built and the hot path is untouched.</param>
/// <param name="PhotonsPerFrame">Total photon budget spread across the casters (per light). Higher = less
/// noisy caustics at more build cost; the map is built once for the static CPU scene.</param>
/// <param name="GatherRadius">Density-estimate radius (and the photon map's spatial-hash cell size).
/// Larger values smooth the caustic (less noise, more blur).</param>
/// <param name="Strength">Overall multiplier applied to the composited caustic irradiance — the absolute
/// scale the plan defers to compositing (emission is normalised to ~1 per light toward each caster).</param>
/// <param name="MaxBounces">Cap on specular interactions a photon follows before it is abandoned.</param>
/// <param name="Quality">The tier this was mapped from, for diagnostics.</param>
public readonly record struct CausticOptions(
    bool Enable = false,
    int PhotonsPerFrame = 200_000,
    float GatherRadius = 0.15f,
    float Strength = 1f,
    int MaxBounces = 8,
    CausticQuality Quality = CausticQuality.Off)
{
    /// <summary>
    /// Maps a quality tier to a photon budget: off on Low/Playable/Medium, modest on High, full on
    /// Ultra (shadows-and-caustics-plan §B5). Callers still gate on <see cref="Enable"/>; the standard
    /// quality presets leave caustics off until the GPU port lands (CPU/GPU parity), so this mapping is
    /// the intended budget for a caustics-enabled preset/scene rather than an automatic switch-on.
    /// </summary>
    public static CausticOptions FromQuality(CausticQuality quality)
    {
        return quality switch
        {
            CausticQuality.High => new CausticOptions(
                Enable: true,
                PhotonsPerFrame: 200_000,
                GatherRadius: 0.15f,
                Strength: 1f,
                Quality: quality),
            CausticQuality.Ultra => new CausticOptions(
                Enable: true,
                PhotonsPerFrame: 600_000,
                GatherRadius: 0.10f,
                Strength: 1f,
                Quality: quality),
            _ => new CausticOptions(
                Enable: false,
                PhotonsPerFrame: 0,
                Quality: quality),
        };
    }
}
