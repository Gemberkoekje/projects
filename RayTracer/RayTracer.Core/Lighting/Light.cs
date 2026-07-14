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
}
