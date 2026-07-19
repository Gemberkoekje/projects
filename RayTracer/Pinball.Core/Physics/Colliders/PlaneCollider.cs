using System;

namespace Pinball.Physics;

/// <summary>
/// A one-sided infinite plane (pinball-plan §6.1.2): the playfield, glass ceiling, flat walls, aprons. The
/// ball lives on the <see cref="Normal"/> side; contact is a signed point-plane distance. Tilting the
/// playfield is done here — a plane whose normal is rotated by the incline α — so gravity stays true-3-D
/// <c>(0,−g,0)</c> and the slope reaction falls out of the contact, never a pre-projected 2-D acceleration.
/// </summary>
public sealed class PlaneCollider : StaticCollider
{
    /// <summary>Unit outward normal (points to the side the ball is on).</summary>
    public Vector3D Normal { get; }
    /// <summary>A point on the plane.</summary>
    public Vector3D Point { get; }

    /// <summary>Constructs a plane through <paramref name="point"/> with outward unit normal <paramref name="normal"/>.</summary>
    public PlaneCollider(Vector3D point, Vector3D normal, PhysicsMaterial material) : base(material)
    {
        Normal = normal.Normalized();
        Point = point;
    }

    /// <summary>
    /// A playfield plane through the origin, tilted by <paramref name="inclineRadians"/> about the world X
    /// axis so its normal is <c>(0, cosα, sinα)</c>: +Z is up-slope, −Z (down-slope) is toward the flippers.
    /// </summary>
    public static PlaneCollider InclinedPlayfield(double inclineRadians, PhysicsMaterial material) =>
        new(Vector3D.Zero, new Vector3D(0, Math.Cos(inclineRadians), Math.Sin(inclineRadians)), material);

    /// <inheritdoc/>
    public override AabbD Bounds => AabbD.Infinite;

    /// <inheritdoc/>
    public override ContactQuery Query(Vector3D ballCenter, double ballRadius)
    {
        double signedDistance = Vector3D.Dot(ballCenter - Point, Normal); // >0 on the ball side
        double separation = signedDistance - ballRadius;
        Vector3D contactPoint = ballCenter - Normal * ballRadius;
        return new ContactQuery(Normal, separation, contactPoint);
    }
}
