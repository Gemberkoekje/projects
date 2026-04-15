namespace RayTracer;

/// <summary>
/// Controls how surface shading is computed in the ray tracer.
/// </summary>
public enum LightingMode
{
    /// <summary>No lighting — raw spectral albedo (original behaviour).</summary>
    None,

    /// <summary>
    /// Ambient + Lambertian direct lighting from point lights.
    /// No shadow rays are cast, so all lights contribute regardless
    /// of occlusion. Cheap but produces no shadows.
    /// </summary>
    Direct,

    /// <summary>
    /// Next Event Estimation: ambient + Lambertian with stochastic
    /// shadow rays. Produces correct hard shadows at the cost of one
    /// extra BVH traversal per sample.
    /// </summary>
    NEE
}
