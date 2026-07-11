namespace RayTracer;

/// <summary>
/// Builds a wall-occupancy bitmap of a <see cref="Maze"/> for the overhead-map overlay
/// (plan §7). The grid is <c>(2W+1)×(2H+1)</c>: odd/odd entries are cell interiors (open),
/// even/odd and odd/even entries are the walls between cells, and even/even are the wall
/// posts. Value 1 = wall, 0 = open. The resolve shader samples this to draw the minimap.
/// </summary>
public static class MazeMinimap
{
    /// <summary>Returns the occupancy grid and its dimensions <c>(2W+1)×(2H+1)</c>.</summary>
    public static (uint[] Grid, int Gw, int Gh) Build(Maze maze)
    {
        ArgumentNullException.ThrowIfNull(maze);
        int gw = 2 * maze.Width + 1;
        int gh = 2 * maze.Height + 1;
        var grid = new uint[gw * gh];

        // Start all-wall, then carve open cells and the passages between them.
        for (int i = 0; i < grid.Length; i++)
            grid[i] = 1u;

        for (int cy = 0; cy < maze.Height; cy++)
        {
            for (int cx = 0; cx < maze.Width; cx++)
            {
                int gx = 2 * cx + 1, gy = 2 * cy + 1;
                grid[gy * gw + gx] = 0u; // cell interior is open

                // A missing wall between two cells means the shared edge is a passage.
                if (!maze.HasWall(cx, cy, Wall.North)) grid[(gy - 1) * gw + gx] = 0u;
                if (!maze.HasWall(cx, cy, Wall.South)) grid[(gy + 1) * gw + gx] = 0u;
                if (!maze.HasWall(cx, cy, Wall.West)) grid[gy * gw + (gx - 1)] = 0u;
                if (!maze.HasWall(cx, cy, Wall.East)) grid[gy * gw + (gx + 1)] = 0u;
            }
        }

        return (grid, gw, gh);
    }
}
