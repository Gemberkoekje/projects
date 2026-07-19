using System;

namespace Pinball.Physics;

/// <summary>
/// A bounded planar rectangle (pinball-plan §6.1.2): wall panels, slingshot faces, aprons. Closest-feature
/// projects the ball centre onto the plane and clamps to the rectangle, so edges and corners resolve too.
/// Two-sided (the normal points to whichever side the ball is on), which is what a thin panel wants.
/// </summary>
public class QuadCollider : ICollider
{
    private readonly Vector3D _edge1Dir;
    private readonly Vector3D _edge2Dir;
    private readonly double _edge1Len;
    private readonly double _edge2Len;

    /// <summary>A corner of the rectangle.</summary>
    public Vector3D Origin { get; }
    /// <summary>First edge vector from <see cref="Origin"/>.</summary>
    public Vector3D Edge1 { get; }
    /// <summary>Second edge vector from <see cref="Origin"/>.</summary>
    public Vector3D Edge2 { get; }

    /// <inheritdoc/>
    public PhysicsMaterial Material { get; }

    /// <summary>Constructs a rectangle spanned by <paramref name="edge1"/> and <paramref name="edge2"/> from <paramref name="origin"/>.</summary>
    public QuadCollider(Vector3D origin, Vector3D edge1, Vector3D edge2, PhysicsMaterial material)
    {
        Origin = origin;
        Edge1 = edge1;
        Edge2 = edge2;
        Material = material;
        _edge1Len = edge1.Length();
        _edge2Len = edge2.Length();
        _edge1Dir = _edge1Len > 1e-12 ? edge1 / _edge1Len : Vector3D.UnitX;
        _edge2Dir = _edge2Len > 1e-12 ? edge2 / _edge2Len : Vector3D.UnitZ;
    }

    /// <inheritdoc/>
    public AabbD Bounds
    {
        get
        {
            Vector3D a = Origin, b = Origin + Edge1, c = Origin + Edge2, d = Origin + Edge1 + Edge2;
            Vector3D min = Vector3D.Min(Vector3D.Min(a, b), Vector3D.Min(c, d));
            Vector3D max = Vector3D.Max(Vector3D.Max(a, b), Vector3D.Max(c, d));
            return new AabbD(min, max);
        }
    }

    /// <inheritdoc/>
    public ContactQuery Query(Vector3D ballCenter, double ballRadius)
    {
        Vector3D d = ballCenter - Origin;
        double u = Math.Clamp(Vector3D.Dot(d, _edge1Dir), 0.0, _edge1Len);
        double w = Math.Clamp(Vector3D.Dot(d, _edge2Dir), 0.0, _edge2Len);
        Vector3D closest = Origin + _edge1Dir * u + _edge2Dir * w;

        Vector3D delta = ballCenter - closest;
        double dist = delta.Length();
        Vector3D normal = dist > 1e-12 ? delta / dist : Vector3D.Cross(_edge1Dir, _edge2Dir).Normalized();
        double separation = dist - ballRadius;
        Vector3D contactPoint = ballCenter - normal * ballRadius;
        return new ContactQuery(normal, separation, contactPoint);
    }

    /// <inheritdoc/>
    public virtual Vector3D SurfaceVelocity(Vector3D worldPoint) => Vector3D.Zero;
}
