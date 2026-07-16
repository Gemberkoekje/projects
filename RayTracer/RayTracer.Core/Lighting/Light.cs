using System.Numerics;

namespace RayTracer;

public class Light
{
    public Vector3 Position { get; init; }

    public Vector3 Color { get; init; }

    public float Ambient { get; init; }

    /// <summary>
    /// Radius of the light as a sphere (world units), for soft shadows (shadows-and-caustics-plan §C1).
    /// <c>0</c> (the default) is an ideal point light — NEE targets the centre and casts a hard shadow,
    /// bit-identical to before. When positive, each NEE sample targets a random point on the sphere, so
    /// the accumulated visibility softens into a penumbra that widens with the radius.
    /// </summary>
    public float Radius { get; init; }

    /// <summary>
    /// A directional (sun) light (shadows-and-caustics-plan §C2): parallel rays with no 1/d² falloff,
    /// the natural source for the open-air half of the maze, god-rays through fog, and skylight prism
    /// rainbows. When <c>true</c>, <see cref="Position"/> is ignored and <see cref="Direction"/> gives
    /// the travel direction of the light; <see cref="Radius"/> is reinterpreted as the sun's angular
    /// radius (radians) for soft shadows.
    /// </summary>
    public bool Directional { get; init; }

    /// <summary>Travel direction of a <see cref="Directional"/> light (the surface is lit from
    /// <c>-Direction</c>). Ignored for a point light. Need not be normalised.</summary>
    public Vector3 Direction { get; init; }

    /// <summary>Blackbody colour temperature (K) of this light's emission (outdoor-area-plan.md §O9): a
    /// warm ~1800 K torch, a ~5500 K daylight sun. <b>0 (default) = flat white</b> — no spectral tint, so
    /// an un-set light renders exactly as before §O9. Applied per hero wavelength in the NEE direct term.</summary>
    public float EmissionTempK { get; init; }

    /// <summary>
    /// Importance weight used to pick this light in NEE (no RNG): a point light's inverse-square,
    /// cosine-gated <c>1/d²</c>, or a directional sun's plain cosine (parallel, no falloff). Zero when
    /// the surface faces away from the light. Matches the old inline weight for point lights exactly.
    /// </summary>
    public float SelectionWeight(Vector3 point, Vector3 normal)
    {
        if (Directional)
        {
            float c = Vector3.Dot(normal, Vector3.Normalize(-Direction));
            return c > 0f ? c : 0f;
        }

        Vector3 toLight = Position - point;
        float dsq = Vector3.Dot(toLight, toLight);
        float dist = MathF.Sqrt(dsq);
        Vector3 ldir = toLight / dist;
        float cos = Vector3.Dot(normal, ldir);
        return cos > 0f ? 1f / MathF.Max(dsq, 1e-6f) : 0f;
    }

    /// <summary>
    /// Samples one shadow ray toward this light from a surface point, writing its direction and length
    /// and returning the geometry factor to weight the contribution by (<c>cos/d²</c> for a point
    /// light, plain <c>cos</c> for a directional sun — no falloff), or 0 when the surface faces away.
    /// Draws RNG only for a soft light: a point light with a <see cref="Radius"/> samples its sphere
    /// (§C1); a directional light with an angular radius samples its disk cone (§C2). A radius-0 light
    /// draws no RNG and reproduces the old hard shadow byte-for-byte.
    /// </summary>
    public float SampleShadowRay(Vector3 point, Vector3 normal, ref uint rng, out Vector3 dir, out float dist)
    {
        if (Directional)
        {
            Vector3 toSun = Vector3.Normalize(-Direction);
            dist = 1e4f; // effectively infinite — any occluder in the scene blocks the sun
            if (toSun.Y <= 0f) { dir = toSun; return 0f; } // §O8: sun below the horizon (night) casts no light
            dir = SampleCone(toSun, ref rng);
            float c = Vector3.Dot(normal, dir);
            return c > 0f ? c : 0f;
        }

        Vector3 toLight = SamplePoint(ref rng) - point;
        float distSq = MathF.Max(Vector3.Dot(toLight, toLight), 1e-6f);
        dist = MathF.Sqrt(distSq);
        dir = toLight / dist;
        float cos = Vector3.Dot(normal, dir);
        return cos > 0f ? cos / distSq : 0f;
    }

    /// <summary>
    /// The shadow-ray target on this light for one NEE sample (shadows-and-caustics-plan §C1): the
    /// centre for a point light (<see cref="Radius"/> == 0), drawing <b>no</b> RNG so a point light's
    /// hard shadow and random stream are byte-identical to before; or a uniform random point on the
    /// spherical light's surface when the radius is positive, so the accumulated NEE visibility softens
    /// into a penumbra. Advances <paramref name="rng"/> by two draws only for an area light.
    /// </summary>
    public Vector3 SamplePoint(ref uint rng)
    {
        if (Radius <= 0f)
            return Position;

        rng = rng * 747796405u + 2891336453u;
        float z = 2f * (rng / 4294967296f) - 1f;
        rng = rng * 747796405u + 2891336453u;
        float phi = 2f * MathF.PI * (rng / 4294967296f);
        float s = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return Position + Radius * new Vector3(s * MathF.Cos(phi), s * MathF.Sin(phi), z);
    }

    // A direction sampled within a cone of half-angle Radius (radians) around a unit axis, for a soft
    // (angular) sun. Radius 0 returns the axis exactly and draws no RNG (a hard parallel shadow).
    private Vector3 SampleCone(Vector3 axis, ref uint rng)
    {
        if (Radius <= 0f)
            return axis;

        rng = rng * 747796405u + 2891336453u;
        float u1 = rng / 4294967296f;
        rng = rng * 747796405u + 2891336453u;
        float u2 = rng / 4294967296f;
        float cosMax = MathF.Cos(Radius);
        float cosT = 1f - u1 * (1f - cosMax);
        float sinT = MathF.Sqrt(MathF.Max(0f, 1f - cosT * cosT));
        float phi = 2f * MathF.PI * u2;
        Vector3 t = MathF.Abs(axis.X) > 0.1f
            ? Vector3.Normalize(new Vector3(axis.Y, -axis.X, 0f))
            : Vector3.Normalize(new Vector3(0f, axis.Z, -axis.Y));
        Vector3 b = Vector3.Cross(axis, t);
        return Vector3.Normalize(sinT * MathF.Cos(phi) * t + sinT * MathF.Sin(phi) * b + cosT * axis);
    }
}
