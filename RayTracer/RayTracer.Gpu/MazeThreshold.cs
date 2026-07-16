using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Frames the passages where the maze crosses the Indoor↔Outdoor boundary (outdoor-area-plan.md §O10) with a
/// stone doorway, so stepping from the roofed maze into the open garden reads as passing through a portal
/// rather than off a torn-open wall. §O4 keeps the boundary walls full stone, and §O1 caps the ceiling
/// there, so the DFS passages through that boundary line are exactly the openings to dress.
///
/// Each opening gets: two <b>jamb</b> posts framing the sides, a <b>lintel</b> header filling the top (its
/// front/back faces are coplanar with the surrounding wall — so it reads as a doorway punched in the wall —
/// while its underside + the jamb inner faces line the reveal), and a low <b>sill</b> step bridging the
/// indoor-stone and outdoor-grass floors. All flat stone (no brick mortar), so the frame reads as trim.
/// Only the current row-band boundary (horizontal grid lines) is handled — the region is always a far-row
/// band (<see cref="Maze.MarkOutdoorRegion"/>), so no vertical Indoor↔Outdoor walls exist.
/// </summary>
internal static class MazeThreshold
{
    private const float JambWidth = 0.16f;    // side-post width as a fraction of CellSize
    private const float LintelBottom = 0.66f; // header underside as a fraction of WallHeight
    private const float SillHeight = 0.05f;   // threshold step height as a fraction of WallHeight
    private const float SillDepth = 0.22f;    // sill reach each side of the boundary as a fraction of CellSize

    internal static List<Tracable> Build(
        Maze maze, MaterialData stone,
        float cellSize = MazeGeometryBuilder.CellSize,
        float wallHeight = MazeGeometryBuilder.WallHeight,
        float wallThickness = 0f)
    {
        var result = new List<Tracable>();
        float cs = cellSize, wh = wallHeight, ht = wallThickness * 0.5f;
        float jw = JambWidth * cs, lb = LintelBottom * wh, sh = SillHeight * wh, sd = SillDepth * cs;

        // Front + back panels on the z±ht faces, spanning x∈[a,b], y∈[c,d] (coplanar with the brick wall).
        void Panel(float a, float b, float c, float d, float z)
        {
            result.Add(new TracableRectangle((new Vector3(a, c, z - ht), new Vector3(b, c, z - ht), new Vector3(a, d, z - ht)), stone));
            result.Add(new TracableRectangle((new Vector3(a, c, z + ht), new Vector3(b, c, z + ht), new Vector3(a, d, z + ht)), stone));
        }
        // A reveal face at x = const spanning the wall thickness and full height (the side of a jamb).
        void SideReveal(float x, float z)
            => result.Add(new TracableRectangle((new Vector3(x, 0, z - ht), new Vector3(x, 0, z + ht), new Vector3(x, wh, z - ht)), stone));
        // The underside of the lintel at y = lb, spanning x∈[a,b] across the thickness (seen walking under it).
        void Underside(float a, float b, float z)
            => result.Add(new TracableRectangle((new Vector3(a, lb, z - ht), new Vector3(b, lb, z - ht), new Vector3(a, lb, z + ht)), stone));

        for (int gy = 1; gy < maze.Height; gy++)
            for (int gx = 0; gx < maze.Width; gx++)
            {
                // A boundary crossing at grid line gy, column gx: one side Indoor, the other Outdoor.
                if (maze.IsOutdoor(gx, gy - 1) == maze.IsOutdoor(gx, gy)) continue;
                // Only frame an actual passage — a walled-off boundary cell keeps its full stone wall.
                if (maze.HasWall(gx, gy, Wall.North)) continue;

                float z = gy * cs, x0 = gx * cs, x1 = (gx + 1) * cs;

                // Jambs (full-height side posts) — front/back trim + the inner reveal facing the opening.
                Panel(x0, x0 + jw, 0f, wh, z);
                Panel(x1 - jw, x1, 0f, wh, z);
                SideReveal(x0 + jw, z);
                SideReveal(x1 - jw, z);

                // Lintel (header) over the opening — front/back trim + its underside.
                Panel(x0 + jw, x1 - jw, lb, wh, z);
                Underside(x0 + jw, x1 - jw, z);

                // Low sill step across the threshold (top + the two risers), bridging the two floor materials.
                float sa = x0 + jw, sb = x1 - jw;
                result.Add(new TracableRectangle((new Vector3(sa, sh, z - sd), new Vector3(sb, sh, z - sd), new Vector3(sa, sh, z + sd)), stone));
                result.Add(new TracableRectangle((new Vector3(sa, 0f, z - sd), new Vector3(sb, 0f, z - sd), new Vector3(sa, sh, z - sd)), stone));
                result.Add(new TracableRectangle((new Vector3(sa, 0f, z + sd), new Vector3(sb, 0f, z + sd), new Vector3(sa, sh, z + sd)), stone));
            }

        return result;
    }
}
