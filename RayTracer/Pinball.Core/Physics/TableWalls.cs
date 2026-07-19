namespace Pinball.Physics;

/// <summary>
/// The shooter-table wall layout — the single source of truth co-registering the render walls
/// (<c>PinballTableScene</c>, via <c>RayTracer.CubicBezier.SweptWall</c>) and the physics colliders
/// (<c>PinballTable</c>, via <see cref="BezierWallCollider"/>). Each wall is a cubic Bézier in <b>render
/// units</b> (X/Z plane; physics multiplies by <c>PinballTable.RenderScale</c> into metres, render uses them
/// as-is), extruded vertically to <see cref="WallHeight"/>. Straight walls use collinear control points.
///
/// <para>This is an in-progress trace of the real <i>3D Pinball — Space Cadet</i> playfield: the right orbit
/// up-and-over the top, the left orbit, and the return/outlane guides at the flippers. Coordinates are a first
/// pass tuned against the top-down debug view (<c>--table-topdown</c>); expect to nudge them. Perimeter
/// (outer box) and the shooter-lane feed live in <c>PinballTable</c>/<see cref="LaneFeed"/>; these are the
/// interior lane walls.</para>
/// </summary>
public static class TableWalls
{
    /// <summary>Wall height in render units (matches the perimeter <c>WallH</c>).</summary>
    public const double WallHeight = 1.3;

    /// <summary>One curved (or straight) wall: four control points in render units + how many panels to
    /// flatten it into. Named for readability while the layout is being dialled in.</summary>
    public readonly record struct Wall(string Name, Vector3D P0, Vector3D P1, Vector3D P2, Vector3D P3, int Segments);

    private static Vector3D P(double x, double z) => new(x, 0, z);

    /// <summary>The single outer wall — the closed playfield boundary the ball can never cross — traced from
    /// the user's grid overlay. Render units, +z up-table, +x right. Five Bézier segments joined end-to-end
    /// into one loop: rounded top, a gentle left side, the bottom, and the right side bulging out to x≈4.6
    /// (the plunger side) up to the top. Everything else (inner guides, props) is stripped for now so the
    /// outer wall can be dialled in on its own.</summary>
    public static readonly Wall[] All =
    {
        // Top arc, centre → right.
        new("outer-top", P(-1.6, 22.9), P(0.6, 23.3), P(2.6, 22.6), P(4.0, 20.8), 16),
        // Right wall: straight down at x≈4.6 (the plunger side).
        new("outer-right", P(4.0, 20.8), P(4.6, 17.0), P(4.6, 8.0), P(4.6, 4.2), 16),
        // Right bottom: 90° kink in to a bottom-right pocket, then it stops — the centre is OPEN (the drain).
        new("outer-right-bottom", P(4.6, 4.2), P(4.5, 2.6), P(3.9, 2.2), P(3.1, 2.5), 10),
        // ── drain gap across the centre (x 3.1 → -3.1): no wall — the ball leaves here ──
        // Left bottom: the ball-saver pocket in the bottom-left corner.
        new("outer-left-bottom", P(-3.1, 2.5), P(-3.9, 2.2), P(-4.3, 3.2), P(-4.0, 5.0), 10),
        // Left wall: up the left side (curve → straight).
        new("outer-left", P(-4.0, 5.0), P(-3.6, 8.0), P(-3.6, 13.0), P(-3.6, 17.0), 16),
        // Left outcrop: bows out at the top-left then back in to meet the top arc.
        new("outer-left-outcrop", P(-3.6, 17.0), P(-3.9, 20.6), P(-2.6, 22.5), P(-1.6, 22.9), 14),
    };

    /// <summary>The wall as a double-precision curve in metres (render units × <paramref name="renderScale"/>).</summary>
    public static CubicBezierD CurveMetres(in Wall w, double renderScale) =>
        new(w.P0 * renderScale, w.P1 * renderScale, w.P2 * renderScale, w.P3 * renderScale);
}
