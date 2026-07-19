using System;

namespace Pinball.Physics;

/// <summary>
/// A finite solid cylinder (pinball-plan §6.1.2): posts, pop-bumper bodies, ball guides. Closest-feature is
/// the exact analytic split into lateral surface, flat cap face, and cap rim, so a ball hitting the side,
/// riding over a cap, or catching the edge all resolve correctly.
/// </summary>
public sealed class CylinderCollider : StaticCollider
{
    /// <summary>Centre of the base cap.</summary>
    public Vector3D Base { get; }
    /// <summary>Unit axis direction (base → top).</summary>
    public Vector3D AxisDir { get; }
    /// <summary>Axial height.</summary>
    public double Height { get; }
    /// <summary>Cylinder radius r₁.</summary>
    public double Radius { get; }

    /// <summary>Constructs a cylinder from its base cap centre, axis direction, height and radius.</summary>
    public CylinderCollider(Vector3D baseCenter, Vector3D axisDir, double height, double radius, PhysicsMaterial material)
        : base(material)
    {
        Base = baseCenter;
        AxisDir = axisDir.Normalized();
        Height = height;
        Radius = radius;
    }

    /// <summary>A vertical post (axis +Y) of the given height standing on <paramref name="footCenter"/>.</summary>
    public static CylinderCollider VerticalPost(Vector3D footCenter, double height, double radius, PhysicsMaterial material) =>
        new(footCenter, Vector3D.UnitY, height, radius, material);

    /// <inheritdoc/>
    public override AabbD Bounds
    {
        get
        {
            Vector3D top = Base + AxisDir * Height;
            var extent = new Vector3D(Radius, Radius, Radius);
            return new AabbD(Vector3D.Min(Base, top) - extent, Vector3D.Max(Base, top) + extent);
        }
    }

    /// <inheritdoc/>
    public override ContactQuery Query(Vector3D ballCenter, double ballRadius)
    {
        Vector3D ap = ballCenter - Base;
        double axial = Vector3D.Dot(ap, AxisDir);
        Vector3D radialVec = ap - AxisDir * axial;
        double radial = radialVec.Length();
        Vector3D radialDir = radial > 1e-12 ? radialVec / radial : ArbitraryPerp(AxisDir);

        Vector3D normal;
        double separation;
        if (axial >= 0.0 && axial <= Height)
        {
            // Beside the tube: the outward normal is always radial (away from the axis).
            normal = radialDir;
            separation = radial - Radius - ballRadius;
        }
        else
        {
            double capAxial = axial < 0.0 ? 0.0 : Height;
            Vector3D capCenter = Base + AxisDir * capAxial;
            if (radial <= Radius)
            {
                // Over a flat cap face: normal is the axis, out the near end.
                normal = AxisDir * (axial < 0.0 ? -1.0 : 1.0);
                separation = Math.Abs(axial - capAxial) - ballRadius;
            }
            else
            {
                // Nearest the cap rim (the circular edge): normal points from that edge to the ball.
                Vector3D rimPoint = capCenter + radialDir * Radius;
                Vector3D delta = ballCenter - rimPoint;
                double dist = delta.Length();
                normal = dist > 1e-12 ? delta / dist : radialDir;
                separation = dist - ballRadius;
            }
        }

        Vector3D contactPoint = ballCenter - normal * ballRadius;
        return new ContactQuery(normal, separation, contactPoint);
    }

    private static Vector3D ArbitraryPerp(Vector3D axis)
    {
        // A stable unit vector perpendicular to the axis (for the degenerate on-axis case).
        Vector3D reference = Math.Abs(axis.Y) < 0.9 ? Vector3D.UnitY : Vector3D.UnitX;
        return Vector3D.Cross(axis, reference).Normalized();
    }
}
