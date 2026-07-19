using System.Collections.Generic;
using System.Numerics;

namespace RayTracer;

/// <summary>
/// A cubic Bézier curve (four control points) and the geometry helpers the renderer needs to author smooth
/// curved surfaces out of the existing analytic primitives. The path tracer has no native curved primitive,
/// so a curve is <b>tessellated</b> into a strip of flat <see cref="TracableRectangle"/> panels
/// (<see cref="SweptWall"/>): with enough segments the facets are far below a ball radius and read as a
/// smooth wall. The matching physics side (<c>Pinball.Physics.BezierWallCollider</c>) tessellates the same
/// control points identically (one <c>RenderScale</c> factor apart), so render and collision stay
/// co-registered. Single precision to match the rest of the render scene (<see cref="System.Numerics.Vector3"/>).
/// </summary>
public readonly struct CubicBezier
{
    /// <summary>Start control point (the curve passes through it at t=0).</summary>
    public readonly Vector3 P0;
    /// <summary>First interior control point (sets the start tangent, <c>3·(P1−P0)</c>).</summary>
    public readonly Vector3 P1;
    /// <summary>Second interior control point (sets the end tangent, <c>3·(P3−P2)</c>).</summary>
    public readonly Vector3 P2;
    /// <summary>End control point (the curve passes through it at t=1).</summary>
    public readonly Vector3 P3;

    /// <summary>Constructs the curve from its four control points.</summary>
    public CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        P0 = p0;
        P1 = p1;
        P2 = p2;
        P3 = p3;
    }

    /// <summary>The point on the curve at parameter <paramref name="t"/> ∈ [0,1] (de Casteljau / Bernstein form).</summary>
    public Vector3 Evaluate(float t)
    {
        float u = 1f - t;
        return (u * u * u) * P0 + (3f * u * u * t) * P1 + (3f * u * t * t) * P2 + (t * t * t) * P3;
    }

    /// <summary>The (unnormalised) tangent d/dt at parameter <paramref name="t"/> ∈ [0,1].</summary>
    public Vector3 Tangent(float t)
    {
        float u = 1f - t;
        return (3f * u * u) * (P1 - P0) + (6f * u * t) * (P2 - P1) + (3f * t * t) * (P3 - P2);
    }

    /// <summary>Samples <paramref name="segments"/>+1 evenly-parametrised points along the curve (a polyline
    /// approximation). <paramref name="segments"/> is clamped to at least 1.</summary>
    public Vector3[] Flatten(int segments)
    {
        if (segments < 1) segments = 1;
        var pts = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
            pts[i] = Evaluate((float)i / segments);
        return pts;
    }

    /// <summary>
    /// Tessellates the curve into a vertical wall: a strip of <paramref name="segments"/>
    /// <see cref="TracableRectangle"/> panels, each spanning one chord of the flattened curve and rising from
    /// <paramref name="yBottom"/> to <paramref name="yTop"/>. The curve is taken in the X/Z plane (its Y is
    /// ignored — the wall's height comes from the two Y bounds), which is exactly what a playfield lane guide
    /// wants. Panels are two-sided when rendered, so the wall is visible from either side.
    /// </summary>
    public IEnumerable<TracableRectangle> SweptWall(float yBottom, float yTop, int segments, MaterialData material)
    {
        Vector3[] pts = Flatten(segments);
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = new(pts[i].X, yBottom, pts[i].Z);
            Vector3 b = new(pts[i + 1].X, yBottom, pts[i + 1].Z);
            Vector3 aTop = new(pts[i].X, yTop, pts[i].Z);
            yield return new TracableRectangle((a, b, aTop), material);
        }
    }
}
