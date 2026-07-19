using System;

namespace Pinball.Physics;

/// <summary>
/// A vertical wall swept along a cubic Bézier curve in the X/Z plane (pinball-plan §6.1.2): smooth curved
/// lane guides — the shooter-lane feed, ramps, horseshoes — that a straight <see cref="QuadCollider"/> cannot
/// shape. The curve is <b>flattened once</b> in the constructor into <see cref="Segments"/> straight panels
/// (each an implicit two-sided quad rising from <see cref="YBottom"/> to <see cref="YTop"/>); the query is
/// then the min over the panels of a <see cref="QuadCollider"/>-style project-and-clamp closest point. A
/// faceted strip — not an analytic curve intersection — is deliberate: it keeps the contact
/// <b>deterministic</b> (fixed segment count, fixed order, no iterative root-finding) and <b>CCD-safe</b>
/// (each panel reports an exact, correctly-signed separation, so conservative advancement never tunnels), and
/// it co-registers exactly with the renderer's <c>CubicBezier.SweptWall</c>, which tessellates the same
/// control points the same way. With a dozen-plus segments the facets sit well under a ball radius and the
/// closest-feature clamp bridges the seams, so the ball feels a smooth wall.
///
/// <para>Two-sided like <see cref="QuadCollider"/> (the normal points to whichever side the ball is on), and
/// optionally rounded by <see cref="HalfThickness"/> (0 = a thin panel; &gt;0 = a rounded rail/wire, whose
/// separation subtracts the half-thickness like <see cref="ArcCollider"/>'s tube).</para>
/// </summary>
public sealed class BezierWallCollider : StaticCollider
{
    private readonly Vector3D[] _origins;   // panel base corners, at YBottom, one per segment
    private readonly Vector3D[] _edgeDir;    // unit horizontal chord direction of each panel
    private readonly double[] _edgeLen;      // chord length of each panel
    private readonly double _height;         // YTop − YBottom
    private readonly double _halfThickness;
    private readonly AabbD _bounds;

    /// <summary>The curve the wall follows (control points in metres, X/Z plane).</summary>
    public CubicBezierD Curve { get; }
    /// <summary>Number of straight panels the curve is flattened into.</summary>
    public int Segments { get; }
    /// <summary>Bottom of the wall's vertical extent (metres).</summary>
    public double YBottom { get; }
    /// <summary>Top of the wall's vertical extent (metres).</summary>
    public double YTop { get; }
    /// <summary>Wall half-thickness (metres): 0 for a thin two-sided panel, &gt;0 for a rounded rail.</summary>
    public double HalfThickness => _halfThickness;

    /// <summary>Builds the swept-wall collider by flattening <paramref name="curve"/> into
    /// <paramref name="segments"/> panels between <paramref name="yBottom"/> and <paramref name="yTop"/>.</summary>
    public BezierWallCollider(CubicBezierD curve, double yBottom, double yTop, int segments,
        double halfThickness, PhysicsMaterial material) : base(material)
    {
        if (segments < 1) segments = 1;
        Curve = curve;
        Segments = segments;
        YBottom = yBottom;
        YTop = yTop;
        _height = yTop - yBottom;
        _halfThickness = halfThickness;

        Vector3D[] pts = curve.Flatten(segments);
        _origins = new Vector3D[segments];
        _edgeDir = new Vector3D[segments];
        _edgeLen = new double[segments];

        double reach = halfThickness;
        var lo = new Vector3D(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var hi = new Vector3D(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        for (int i = 0; i < segments; i++)
        {
            // Panel base corner, on the curve at YBottom (the curve's own Y is ignored — the wall's height is
            // the [YBottom,YTop] extent), and the horizontal chord to the next sample.
            Vector3D a = new(pts[i].X, yBottom, pts[i].Z);
            Vector3D b = new(pts[i + 1].X, yBottom, pts[i + 1].Z);
            Vector3D edge = b - a;
            double len = edge.Length();
            _origins[i] = a;
            _edgeLen[i] = len;
            _edgeDir[i] = len > 1e-12 ? edge / len : Vector3D.UnitX;

            // Grow the bound over both base corners; the top corners only differ in Y (added below).
            lo = Vector3D.Min(lo, Vector3D.Min(a, b));
            hi = Vector3D.Max(hi, Vector3D.Max(a, b));
        }
        var extent = new Vector3D(reach, reach, reach);
        _bounds = new AabbD(
            new Vector3D(lo.X, yBottom, lo.Z) - extent,
            new Vector3D(hi.X, yTop, hi.Z) + extent);
    }

    /// <inheritdoc/>
    public override AabbD Bounds => _bounds;

    /// <inheritdoc/>
    public override ContactQuery Query(Vector3D ballCenter, double ballRadius)
    {
        // Closest point over all panels: for each, project-and-clamp along its horizontal chord (u) and its
        // vertical extent (w), exactly like QuadCollider, and keep the nearest. A linear scan in a fixed panel
        // order keeps the result deterministic.
        double bestDistSq = double.PositiveInfinity;
        Vector3D bestDelta = Vector3D.UnitY;
        int bestSeg = 0;
        for (int i = 0; i < _origins.Length; i++)
        {
            Vector3D d = ballCenter - _origins[i];
            double u = Math.Clamp(Vector3D.Dot(d, _edgeDir[i]), 0.0, _edgeLen[i]);
            double w = Math.Clamp(d.Y, 0.0, _height); // edge2 is +Y (unit), so the projection is just d.Y
            Vector3D closest = _origins[i] + _edgeDir[i] * u + Vector3D.UnitY * w;
            Vector3D delta = ballCenter - closest;
            double distSq = delta.LengthSquared();
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestDelta = delta;
                bestSeg = i;
            }
        }

        double dist = Math.Sqrt(bestDistSq);
        // Degenerate normal (ball exactly on the wall): the panel's horizontal outward normal, like the
        // fallbacks in QuadCollider/ArcCollider.
        Vector3D normal = dist > 1e-12
            ? bestDelta / dist
            : Vector3D.Cross(_edgeDir[bestSeg], Vector3D.UnitY).Normalized();
        double separation = dist - _halfThickness - ballRadius;
        Vector3D contactPoint = ballCenter - normal * ballRadius;
        return new ContactQuery(normal, separation, contactPoint);
    }
}
