using System;

namespace Pinball.Physics;

/// <summary>
/// A capsule collider — a segment swept by a tube of radius <see cref="Radius"/> (pinball-plan §6.1.2):
/// rubbers, rails, wireforms, and the flipper bat. Contact is distance(point→segment) vs <c>R+r</c>. The
/// closest-feature query is closed-form (project the centre onto the clamped segment), so it is exact and
/// cheap. A flipper reuses this with a non-zero surface velocity (§6.1.6) — see its kinematic subclass.
/// </summary>
public class CapsuleCollider : ICollider
{
    /// <summary>First segment endpoint (the tube axis).</summary>
    public Vector3D PointA { get; protected set; }
    /// <summary>Second segment endpoint.</summary>
    public Vector3D PointB { get; protected set; }
    /// <summary>Tube radius R.</summary>
    public double Radius { get; }

    /// <inheritdoc/>
    public PhysicsMaterial Material { get; }

    /// <summary>Constructs a capsule from endpoints <paramref name="a"/>→<paramref name="b"/> and tube radius <paramref name="radius"/>.</summary>
    public CapsuleCollider(Vector3D a, Vector3D b, double radius, PhysicsMaterial material)
    {
        PointA = a;
        PointB = b;
        Radius = radius;
        Material = material;
    }

    /// <inheritdoc/>
    public AabbD Bounds
    {
        get
        {
            var extent = new Vector3D(Radius, Radius, Radius);
            return new AabbD(Vector3D.Min(PointA, PointB) - extent, Vector3D.Max(PointA, PointB) + extent);
        }
    }

    /// <summary>The point on the capsule's axis segment closest to <paramref name="p"/> (clamped to the ends).</summary>
    protected Vector3D ClosestOnAxis(Vector3D p)
    {
        Vector3D ab = PointB - PointA;
        double lenSq = ab.LengthSquared();
        if (lenSq < 1e-18)
            return PointA; // degenerate segment ⇒ a sphere
        double t = Vector3D.Dot(p - PointA, ab) / lenSq;
        t = Math.Clamp(t, 0.0, 1.0);
        return PointA + ab * t;
    }

    /// <inheritdoc/>
    public ContactQuery Query(Vector3D ballCenter, double ballRadius)
    {
        Vector3D closest = ClosestOnAxis(ballCenter);
        Vector3D delta = ballCenter - closest;
        double dist = delta.Length();
        Vector3D normal = dist > 1e-12 ? delta / dist : Vector3D.UnitY;
        double separation = dist - (Radius + ballRadius);
        Vector3D contactPoint = ballCenter - normal * ballRadius;
        return new ContactQuery(normal, separation, contactPoint);
    }

    /// <inheritdoc/>
    public virtual Vector3D SurfaceVelocity(Vector3D worldPoint) => Vector3D.Zero;
}
