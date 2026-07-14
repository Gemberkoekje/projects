using System.Numerics;

namespace RayTracer;

/// <summary>
/// A scene primitive that can be intersected by a ray. Implementations return a
/// <see cref="HitInfo"/> describing the closest forward hit, or <c>null</c> for a miss.
/// </summary>
public interface Tracable
{
    /// <summary>Axis-aligned bounds used to build the acceleration structure.</summary>
    AABB Bounds { get; }

    /// <summary>
    /// How this primitive interacts with light, exposed without an intersection so the caustic
    /// pass can enumerate the specular casters (glass/jewels/bubbles) and aim photons at their
    /// bounds (shadows-and-caustics-plan §B). Defaults to <see cref="SurfaceKind.Diffuse"/>;
    /// primitives that carry a material override it with their material's surface.
    /// </summary>
    SurfaceKind Surface => SurfaceKind.Diffuse;

    /// <summary>
    /// Intersects <paramref name="ray"/> with this primitive, returning the hit
    /// (geometry, shading, and optical channels) or <c>null</c> if the ray misses.
    /// </summary>
    HitInfo? Intersect(Ray ray);
}
