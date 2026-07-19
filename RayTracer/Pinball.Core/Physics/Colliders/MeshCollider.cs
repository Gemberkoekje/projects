using System;

namespace Pinball.Physics;

/// <summary>
/// A triangle-soup collider (pinball-plan §6.1.2): ramps, sculpted plastics, habitrails. Closest-feature is
/// the nearest point over all triangles (two-sided). Slice uses a linear scan over the triangle list, which
/// is fine for the small ramp meshes a table needs; a per-mesh BVH is the documented later optimisation.
/// </summary>
public sealed class MeshCollider : StaticCollider
{
    private readonly (Vector3D A, Vector3D B, Vector3D C)[] _triangles;
    private readonly AabbD _bounds;

    /// <summary>Constructs a mesh collider from a triangle list.</summary>
    public MeshCollider((Vector3D A, Vector3D B, Vector3D C)[] triangles, PhysicsMaterial material) : base(material)
    {
        System.ArgumentNullException.ThrowIfNull(triangles);
        _triangles = triangles;

        Vector3D min = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        Vector3D max = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (var (a, b, c) in triangles)
        {
            min = Vector3D.Min(min, Vector3D.Min(a, Vector3D.Min(b, c)));
            max = Vector3D.Max(max, Vector3D.Max(a, Vector3D.Max(b, c)));
        }
        _bounds = triangles.Length > 0 ? new AabbD(min, max) : new AabbD(Vector3D.Zero, Vector3D.Zero);
    }

    /// <inheritdoc/>
    public override AabbD Bounds => _bounds;

    /// <inheritdoc/>
    public override ContactQuery Query(Vector3D ballCenter, double ballRadius)
    {
        if (_triangles.Length == 0)
            // No geometry ⇒ no contact. Report a large positive separation so nothing treats the ball's own
            // centre as a penetration (a bare `bestPoint = ballCenter` would fabricate a −r contact).
            return new ContactQuery(Vector3D.UnitY, double.PositiveInfinity, ballCenter);

        double bestDistSq = double.PositiveInfinity;
        Vector3D bestPoint = ballCenter;
        for (int i = 0; i < _triangles.Length; i++)
        {
            var (a, b, c) = _triangles[i];
            Vector3D p = ClosestOnTriangle(ballCenter, a, b, c);
            double dsq = (ballCenter - p).LengthSquared();
            if (dsq < bestDistSq)
            {
                bestDistSq = dsq;
                bestPoint = p;
            }
        }

        Vector3D delta = ballCenter - bestPoint;
        double dist = delta.Length();
        Vector3D normal = dist > 1e-12 ? delta / dist : Vector3D.UnitY;
        double separation = dist - ballRadius;
        Vector3D contactPoint = ballCenter - normal * ballRadius;
        return new ContactQuery(normal, separation, contactPoint);
    }

    /// <summary>The closest point on triangle <paramref name="a"/><paramref name="b"/><paramref name="c"/> to <paramref name="p"/> (Ericson, Real-Time Collision Detection).</summary>
    public static Vector3D ClosestOnTriangle(Vector3D p, Vector3D a, Vector3D b, Vector3D c)
    {
        Vector3D ab = b - a, ac = c - a, ap = p - a;
        double d1 = Vector3D.Dot(ab, ap), d2 = Vector3D.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        Vector3D bp = p - b;
        double d3 = Vector3D.Dot(ab, bp), d4 = Vector3D.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));

        Vector3D cp = p - c;
        double d5 = Vector3D.Dot(ab, cp), d6 = Vector3D.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));

        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        double denom = 1.0 / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }
}
