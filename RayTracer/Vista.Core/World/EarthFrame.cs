using Vista.Scene;

namespace Vista.World;

/// <summary>
/// The frame the terrain is meshed and traced in since M1.9 (vista-plan §10 M1.9): the world bent onto the Earth around the
/// camera. Its origin is the point of the ground sphere straight below the camera (the tangent point), +Y is the camera's
/// vertical, +X east and +Z north there; the planet's centre is at <c>(0, −R, 0)</c> with <c>R</c> the ground sphere's radius.
/// A world point <c>(x, h, z)</c> — <c>h</c> above the ground sphere at a horizontal distance <c>s</c> from the camera — keeps
/// its height and its bearing and sits on the sphere <c>s</c> along the ground: it sinks by the curvature drop
/// <c>2R·sin²(s/2R)</c> (785 m at 100 km) and its vertical tilts by <c>s/R</c>. The camera sits at <c>(0, h, 0)</c>, its axes
/// unchanged, so coordinates near it are small and keep their precision in float.
///
/// <para>M1.4 bent the world only for the air (its <c>Bend</c>); the terrain was traced flat. Since M1.9 the mesh itself is
/// bent, because its far field reaches past the horizon (hundreds of kilometres), where a flat world would put the far ranges
/// kilometres too high. The shader's <c>HeightAbove</c> and <c>LocalUp</c> (<c>VistaIntegrator.hlsl</c>) read heights and
/// verticals back out of a frame position; <see cref="HeightAbove"/> and <see cref="LocalUp"/> are their references.</para>
/// </summary>
/// <param name="GroundRadius">Radius of the ground sphere (the sea-level radius plus the scene's base altitude), m.</param>
/// <param name="TangentX">World x of the camera, m.</param>
/// <param name="TangentZ">World z of the camera, m.</param>
public readonly record struct EarthFrame(double GroundRadius, double TangentX, double TangentZ)
{
    /// <summary>The frame position of world point <c>(x, h, z)</c>; heights below the ground sphere stay below it.</summary>
    public Vector3D ToFrame(double x, double h, double z)
    {
        double dx = x - TangentX, dz = z - TangentZ;
        double s = Math.Sqrt(dx * dx + dz * dz);
        if (s == 0.0)
            return new Vector3D(0.0, h, 0.0);
        double theta = s / GroundRadius;
        double half = Math.Sin(0.5 * theta);
        double drop = 2.0 * GroundRadius * half * half;
        double horizontal = (GroundRadius + h) * Math.Sin(theta);
        return new Vector3D(dx / s * horizontal, h * Math.Cos(theta) - drop, dz / s * horizontal);
    }

    /// <summary>
    /// The height above the ground sphere of frame point <paramref name="p"/>: <c>|p − c| − R</c> written as
    /// <c>(x² + z² + y(y + 2R)) / (|p − c| + R)</c>, which keeps its precision where the subtraction would not.
    /// </summary>
    public double HeightAbove(Vector3D p)
    {
        double r = GroundRadius;
        double horizontal2 = p.X * p.X + p.Z * p.Z;
        double distance = Math.Sqrt(horizontal2 + (p.Y + r) * (p.Y + r));
        return (horizontal2 + p.Y * (p.Y + 2.0 * r)) / (distance + r);
    }

    /// <summary>The local vertical at frame point <paramref name="p"/>: away from the planet's centre.</summary>
    public Vector3D LocalUp(Vector3D p) => new Vector3D(p.X, p.Y + GroundRadius, p.Z).Normalized();

    /// <summary>
    /// How far the camera at height <paramref name="cameraHeight"/> could see terrain as high as <paramref name="terrainHigh"/>
    /// over the curve of the ground sphere: its horizon distance plus the terrain's, m.
    /// </summary>
    public double HorizonReach(double cameraHeight, double terrainHigh)
    {
        static double Horizon(double r, double h) => h > 0.0 ? Math.Sqrt(h * (2.0 * r + h)) : 0.0;
        return Horizon(GroundRadius, cameraHeight) + Horizon(GroundRadius, terrainHigh);
    }
}
