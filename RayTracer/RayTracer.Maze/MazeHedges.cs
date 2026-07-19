using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Replaces the full-height stone walls of the outdoor half (outdoor-area-plan.md §O4) with <b>low
/// hedges</b> — a wall is a hedge only when every in-grid cell it borders is outdoor, so interior
/// outdoor↔outdoor walls and the outdoor perimeter become waist-high foliage you can see over, while
/// the indoor↔outdoor boundary keeps its full stone wall (the threshold from maze into garden).
/// Follows the <see cref="MazeWindows"/> pattern: <see cref="SkipPredicate"/> tells
/// <see cref="MazeGeometryBuilder"/> to omit the tall wall, and <see cref="Build"/> adds the low
/// geometry in its place.
/// </summary>
internal static class MazeHedges
{
    private const float HeightFraction = 0.55f; // hedge height as a fraction of the wall height

    // A horizontal wall (grid line z = gy·cs, between cells (gx,gy-1) and (gx,gy)) is a hedge when every
    // in-grid neighbour is outdoor and at least one neighbour exists and is outdoor.
    private static bool HedgeH(Maze m, int gx, int gy)
    {
        bool inA = gy > 0, inB = gy < m.Height;
        bool aOut = !inA || m.IsOutdoor(gx, gy - 1);
        bool bOut = !inB || m.IsOutdoor(gx, gy);
        bool anyOut = (inA && m.IsOutdoor(gx, gy - 1)) || (inB && m.IsOutdoor(gx, gy));
        return aOut && bOut && anyOut;
    }

    private static bool HedgeV(Maze m, int gx, int gy)
    {
        bool inA = gx > 0, inB = gx < m.Width;
        bool aOut = !inA || m.IsOutdoor(gx - 1, gy);
        bool bOut = !inB || m.IsOutdoor(gx, gy);
        bool anyOut = (inA && m.IsOutdoor(gx - 1, gy)) || (inB && m.IsOutdoor(gx, gy));
        return aOut && bOut && anyOut;
    }

    /// <summary>The <c>skipWall</c> predicate: true for the walls a hedge replaces (so the tall wall is
    /// omitted). Compose with any other skip (e.g. windows) via a boolean OR at the call site.</summary>
    internal static Func<int, int, bool, bool> SkipPredicate(Maze maze)
        => (gx, gy, horizontal) => horizontal ? HedgeH(maze, gx, gy) : HedgeV(maze, gx, gy);

    /// <summary>The set of wall ids (<c>gx, gy, horizontal</c>) that are hedges — i.e. purely between
    /// Outdoor cells. Indoor↔Outdoor boundary walls are <b>not</b> included (they stay full stone), so
    /// indoor-decor features (signs, mirrors, windows) can exclude these to stay out of the open garden
    /// while still landing on the boundary.</summary>
    internal static HashSet<(int, int, bool)> HedgeWalls(Maze maze)
    {
        var set = new HashSet<(int, int, bool)>();
        for (int gy = 0; gy <= maze.Height; gy++)
            for (int gx = 0; gx < maze.Width; gx++)
                if (HedgeH(maze, gx, gy)) set.Add((gx, gy, true));
        for (int gx = 0; gx <= maze.Width; gx++)
            for (int gy = 0; gy < maze.Height; gy++)
                if (HedgeV(maze, gx, gy)) set.Add((gx, gy, false));
        return set;
    }

    /// <summary>
    /// Builds the low hedge quads — one per wall the <see cref="SkipPredicate"/> removed — at
    /// <see cref="HeightFraction"/> of the wall height, using the same wall enumeration as
    /// <see cref="MazeGeometryBuilder"/> so positions line up exactly.
    /// </summary>
    internal static List<Tracable> Build(
        Maze maze, MaterialData hedge,
        float cellSize = MazeGeometryBuilder.CellSize,
        float wallHeight = MazeGeometryBuilder.WallHeight)
    {
        var result = new List<Tracable>();
        float cs = cellSize;
        float h = wallHeight * HeightFraction;

        // Horizontal walls (perpendicular to Z), matching MazeGeometryBuilder's North/South convention.
        for (int gy = 0; gy <= maze.Height; gy++)
            for (int gx = 0; gx < maze.Width; gx++)
            {
                bool hasWall = gy < maze.Height
                    ? maze.HasWall(gx, gy, Wall.North)
                    : maze.HasWall(gx, maze.Height - 1, Wall.South);
                if (hasWall && HedgeH(maze, gx, gy))
                {
                    float z = gy * cs, x0 = gx * cs, x1 = (gx + 1) * cs;
                    result.Add(new TracableRectangle(
                        (new Vector3(x0, 0, z), new Vector3(x1, 0, z), new Vector3(x0, h, z)), hedge));
                }
            }

        // Vertical walls (perpendicular to X), matching the West/East convention.
        for (int gx = 0; gx <= maze.Width; gx++)
            for (int gy = 0; gy < maze.Height; gy++)
            {
                bool hasWall = gx < maze.Width
                    ? maze.HasWall(gx, gy, Wall.West)
                    : maze.HasWall(maze.Width - 1, gy, Wall.East);
                if (hasWall && HedgeV(maze, gx, gy))
                {
                    float x = gx * cs, z0 = gy * cs, z1 = (gy + 1) * cs;
                    result.Add(new TracableRectangle(
                        (new Vector3(x, 0, z0), new Vector3(x, 0, z1), new Vector3(x, h, z0)), hedge));
                }
            }

        return result;
    }
}
