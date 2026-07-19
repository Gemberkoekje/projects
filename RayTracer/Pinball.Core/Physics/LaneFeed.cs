namespace Pinball.Physics;

/// <summary>
/// The shooter-lane feed curve — the single source of truth co-registering the render wall
/// (<c>PinballTableScene</c>, via <c>RayTracer.CubicBezier.SweptWall</c>) with the physics guide
/// (<c>PinballTable</c>, via <see cref="BezierWallCollider"/>). Control points are in <b>render units</b>
/// (the authoring space; physics multiplies by <c>PinballTable.RenderScale</c> into metres, render uses them
/// as-is), in the X/Z plane. A ball launched up the shooter lane (+z at x≈5.05) meets this quarter-turn near
/// the top and is swept left-and-down across the divider line (x=4.6) into the open playfield, where the
/// down-slope gravity carries it into play. Keeping the four points here means the render panels and the
/// collider panels are always the same curve, one scale factor apart.
/// </summary>
public static class LaneFeed
{
    /// <summary>Entry, against the right wall — the rising ball first meets the wall here (tangent ≈ +z).</summary>
    public static readonly Vector3D P0 = new(5.5, 0, 14.6);
    /// <summary>Holds the +z entry tangent so the ball slides smoothly onto the curve.</summary>
    public static readonly Vector3D P1 = new(5.5, 0, 15.8);
    /// <summary>Bends the curve over the top toward −x.</summary>
    public static readonly Vector3D P2 = new(4.9, 0, 16.4);
    /// <summary>Exit, left of the divider line (x=4.6) heading −x into the playfield.</summary>
    public static readonly Vector3D P3 = new(4.0, 0, 16.3);

    /// <summary>Wall height in render units (matches the lane divider / side walls, <c>WallH</c>).</summary>
    public const double WallHeight = 1.3;

    /// <summary>Panel count the curve is flattened into on both sides — enough that a ball never feels the facets.</summary>
    public const int Segments = 14;

    /// <summary>The feed as a double-precision curve in metres (render units × <paramref name="renderScale"/>),
    /// ready for a <see cref="BezierWallCollider"/>.</summary>
    public static CubicBezierD CurveMetres(double renderScale) =>
        new(P0 * renderScale, P1 * renderScale, P2 * renderScale, P3 * renderScale);
}
