namespace Pinball.Physics;

/// <summary>
/// A solid sphere collider (pinball-plan §6.1.2): rollover bumper caps, captured balls, jewels. Contact is
/// centre-distance vs <c>r₁+r</c>.
/// </summary>
public sealed class SphereCollider : StaticCollider
{
    /// <summary>Centre of the sphere.</summary>
    public Vector3D Center { get; }
    /// <summary>Radius r₁ of the sphere.</summary>
    public double Radius { get; }

    /// <summary>Constructs a sphere collider.</summary>
    public SphereCollider(Vector3D center, double radius, PhysicsMaterial material) : base(material)
    {
        Center = center;
        Radius = radius;
    }

    /// <inheritdoc/>
    public override AabbD Bounds
    {
        get
        {
            var extent = new Vector3D(Radius, Radius, Radius);
            return new AabbD(Center - extent, Center + extent);
        }
    }

    /// <inheritdoc/>
    public override ContactQuery Query(Vector3D ballCenter, double ballRadius)
    {
        Vector3D delta = ballCenter - Center;
        double dist = delta.Length();
        // Degenerate coincident centres: pick an arbitrary but stable normal so the solver can eject.
        Vector3D normal = dist > 1e-12 ? delta / dist : Vector3D.UnitY;
        double separation = dist - (Radius + ballRadius);
        Vector3D contactPoint = ballCenter - normal * ballRadius;
        return new ContactQuery(normal, separation, contactPoint);
    }
}
