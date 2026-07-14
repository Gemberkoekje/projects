using System.Numerics;

namespace RayTracer;

/// <summary>
/// An analytic sphere (spectral-effects-plan.md §0.4): an exact ray/sphere intersection with
/// a smooth outward normal, so a ball is genuinely round rather than a faceted quad-sphere
/// (whose per-facet flat normals would band any normal-sensitive shading, e.g. thin-film).
/// The BVH accepts it like any other <see cref="Tracable"/> via its axis-aligned
/// <see cref="Bounds"/> — no BVH change. The GPU carries it as procedural-AABB geometry
/// (<c>GpuScenePacker</c> → <c>GpuSphere</c>, intersected in <c>PathTracePhase6.hlsl</c>).
/// </summary>
public sealed class Sphere(Vector3 center, float radius, MaterialData material) : Tracable
{
    /// <summary>World-space centre.</summary>
    public Vector3 Center { get; } = center;
    /// <summary>Radius (world units).</summary>
    public float Radius { get; } = radius;
    /// <summary>Surface material (diffuse, dielectric, thin-film, …).</summary>
    public MaterialData Material { get; } = material;

    private readonly AABB _bounds = new(center - new Vector3(radius), center + new Vector3(radius));

    /// <inheritdoc/>
    public AABB Bounds => _bounds;

    /// <inheritdoc/>
    public SurfaceKind Surface => Material.Surface;

    /// <inheritdoc/>
    public HitInfo? Intersect(Ray ray)
    {
        // Solve |O + t·D − C|² = R² for the smallest t ≥ tMin (half-b form).
        Vector3 oc = ray.Origin - Center;
        Vector3 d = ray.Direction;
        float a = Vector3.Dot(d, d);
        float halfB = Vector3.Dot(oc, d);
        float c = Vector3.Dot(oc, oc) - Radius * Radius;
        float disc = halfB * halfB - a * c;
        if (disc < 0f)
            return null;

        float sqrtDisc = MathF.Sqrt(disc);
        const float tMin = 1e-4f;
        float t = (-halfB - sqrtDisc) / a;   // near root (front face)
        if (t < tMin)
        {
            t = (-halfB + sqrtDisc) / a;      // far root (ray origin is inside the sphere)
            if (t < tMin)
                return null;
        }

        Vector3 hit = ray.Origin + d * t;
        Vector3 outward = (hit - Center) / Radius;                        // smooth analytic normal
        Vector3 faceNormal = Vector3.Dot(d, outward) < 0f ? outward : -outward; // oriented against the ray
        return HitInfo.FromMaterial(t, hit, faceNormal, null,
            Material.GetSpectralReflectance((int)ray.Wavelength), Material, (int)ray.Wavelength, d);
    }
}
