using System;

namespace Pinball.Physics;

/// <summary>
/// A swept-arc collider (pinball-plan §6.1.2): curved lane guides and horseshoes — a circular arc in a plane
/// swept by a tube of radius <see cref="TubeRadius"/>. Contact is the distance to the (angle-clamped) arc vs
/// <c>tube + r</c>, closed-form via an <c>atan2</c> in the arc's plane.
/// </summary>
public sealed class ArcCollider : StaticCollider
{
    private readonly Vector3D _u; // in-plane basis at angle 0
    private readonly Vector3D _v; // in-plane basis at angle π/2

    /// <summary>Centre of the arc's circle.</summary>
    public Vector3D Center { get; }
    /// <summary>Arc radius (centre → tube axis).</summary>
    public double Radius { get; }
    /// <summary>Tube radius swept along the arc.</summary>
    public double TubeRadius { get; }
    /// <summary>Start angle (rad) in the arc plane.</summary>
    public double StartAngle { get; }
    /// <summary>Angular sweep (rad, may be negative).</summary>
    public double SweepAngle { get; }

    /// <summary>Constructs an arc in the plane spanned by <paramref name="axis0"/> (angle 0) and <paramref name="axis90"/> (angle π/2).</summary>
    public ArcCollider(Vector3D center, Vector3D axis0, Vector3D axis90, double radius, double tubeRadius,
        double startAngle, double sweepAngle, PhysicsMaterial material) : base(material)
    {
        Center = center;
        _u = axis0.Normalized();
        _v = axis90.Normalized();
        Radius = radius;
        TubeRadius = tubeRadius;
        StartAngle = startAngle;
        SweepAngle = sweepAngle;
    }

    /// <inheritdoc/>
    public override AabbD Bounds
    {
        get
        {
            double reach = Radius + TubeRadius;
            var extent = new Vector3D(reach, reach, reach);
            return new AabbD(Center - extent, Center + extent);
        }
    }

    /// <inheritdoc/>
    public override ContactQuery Query(Vector3D ballCenter, double ballRadius)
    {
        Vector3D local = ballCenter - Center;
        double pu = Vector3D.Dot(local, _u);
        double pv = Vector3D.Dot(local, _v);
        double angle = Math.Atan2(pv, pu);

        // Clamp to the swept range (either direction). atan2 is (−π,π]; shift it by whole turns to the
        // representative nearest the arc's angular centre, THEN clamp — so a ball just past either endpoint
        // resolves to that endpoint, correctly even when the arc spans more than π (a naive clamp would
        // otherwise pick the far endpoint and miss the contact — a tunnelling bug).
        double lo = Math.Min(StartAngle, StartAngle + SweepAngle);
        double hi = Math.Max(StartAngle, StartAngle + SweepAngle);
        double mid = (lo + hi) * 0.5;
        double representative = angle + Math.Tau * Math.Round((mid - angle) / Math.Tau);
        angle = Math.Clamp(representative, lo, hi);

        Vector3D arcPoint = Center + (_u * Math.Cos(angle) + _v * Math.Sin(angle)) * Radius;
        Vector3D delta = ballCenter - arcPoint;
        double dist = delta.Length();
        Vector3D normal = dist > 1e-12 ? delta / dist : _u;
        double separation = dist - TubeRadius - ballRadius;
        Vector3D contactPoint = ballCenter - normal * ballRadius;
        return new ContactQuery(normal, separation, contactPoint);
    }
}
