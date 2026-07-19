namespace Pinball.Physics;

/// <summary>
/// A double-precision cubic Bézier curve (four control points) for the physics engine — the deterministic
/// twin of the renderer's <c>RayTracer.CubicBezier</c>. Used to author curved lane guides (see
/// <see cref="BezierWallCollider"/>). Kept in double precision like the rest of the narrowphase so the
/// closed-form §6.1.8 tests replay bit-for-bit. Co-registration with the render side is by construction:
/// the same control-point numbers, scaled by <c>PinballTable.RenderScale</c> into metres, flattened with the
/// same segment count and the same evenly-spaced parameters.
/// </summary>
public readonly struct CubicBezierD
{
    /// <summary>Start control point (t=0).</summary>
    public readonly Vector3D P0;
    /// <summary>First interior control point (start tangent).</summary>
    public readonly Vector3D P1;
    /// <summary>Second interior control point (end tangent).</summary>
    public readonly Vector3D P2;
    /// <summary>End control point (t=1).</summary>
    public readonly Vector3D P3;

    /// <summary>Constructs the curve from its four control points.</summary>
    public CubicBezierD(Vector3D p0, Vector3D p1, Vector3D p2, Vector3D p3)
    {
        P0 = p0;
        P1 = p1;
        P2 = p2;
        P3 = p3;
    }

    /// <summary>The point on the curve at parameter <paramref name="t"/> ∈ [0,1] (Bernstein form).</summary>
    public Vector3D Evaluate(double t)
    {
        double u = 1.0 - t;
        return (u * u * u) * P0 + (3.0 * u * u * t) * P1 + (3.0 * u * t * t) * P2 + (t * t * t) * P3;
    }

    /// <summary>The (unnormalised) tangent d/dt at parameter <paramref name="t"/> ∈ [0,1].</summary>
    public Vector3D Tangent(double t)
    {
        double u = 1.0 - t;
        return (3.0 * u * u) * (P1 - P0) + (6.0 * u * t) * (P2 - P1) + (3.0 * t * t) * (P3 - P2);
    }

    /// <summary>Samples <paramref name="segments"/>+1 evenly-parametrised points along the curve (a polyline
    /// approximation). <paramref name="segments"/> is clamped to at least 1.</summary>
    public Vector3D[] Flatten(int segments)
    {
        if (segments < 1) segments = 1;
        var pts = new Vector3D[segments + 1];
        for (int i = 0; i <= segments; i++)
            pts[i] = Evaluate((double)i / segments);
        return pts;
    }
}
