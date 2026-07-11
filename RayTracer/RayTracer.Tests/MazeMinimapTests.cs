using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Pins the overhead-map occupancy grid (plan §7): correct dimensions, open cell interiors,
/// walled boundary, and passages that agree with the maze's wall flags.
/// </summary>
[TestClass]
public sealed class MazeMinimapTests
{
    [TestMethod]
    public void Build_HasExpectedDimensions()
    {
        var maze = new Maze(8, 6, seed: 3);
        var (grid, gw, gh) = MazeMinimap.Build(maze);
        Assert.AreEqual(2 * 8 + 1, gw);
        Assert.AreEqual(2 * 6 + 1, gh);
        Assert.AreEqual(gw * gh, grid.Length);
    }

    [TestMethod]
    public void CellInteriorsAreOpen()
    {
        var maze = new Maze(10, 10, seed: 42);
        var (grid, gw, _) = MazeMinimap.Build(maze);
        for (int cy = 0; cy < maze.Height; cy++)
            for (int cx = 0; cx < maze.Width; cx++)
                Assert.AreEqual(0u, grid[(2 * cy + 1) * gw + (2 * cx + 1)],
                    $"Cell ({cx},{cy}) interior should be open.");
    }

    [TestMethod]
    public void OuterBoundaryIsWall()
    {
        var maze = new Maze(6, 6, seed: 7);
        var (grid, gw, gh) = MazeMinimap.Build(maze);
        for (int x = 0; x < gw; x++)
        {
            Assert.AreEqual(1u, grid[0 * gw + x], "Top boundary must be wall.");
            Assert.AreEqual(1u, grid[(gh - 1) * gw + x], "Bottom boundary must be wall.");
        }
        for (int y = 0; y < gh; y++)
        {
            Assert.AreEqual(1u, grid[y * gw + 0], "Left boundary must be wall.");
            Assert.AreEqual(1u, grid[y * gw + (gw - 1)], "Right boundary must be wall.");
        }
    }

    [TestMethod]
    public void PassagesMatchWallFlags()
    {
        var maze = new Maze(8, 8, seed: 123);
        var (grid, gw, _) = MazeMinimap.Build(maze);
        for (int cy = 0; cy < maze.Height; cy++)
        {
            for (int cx = 0; cx < maze.Width; cx++)
            {
                int gx = 2 * cx + 1, gy = 2 * cy + 1;
                // Where a wall flag is absent, the shared edge cell must be open.
                if (!maze.HasWall(cx, cy, Wall.East))
                    Assert.AreEqual(0u, grid[gy * gw + (gx + 1)], $"East passage of ({cx},{cy}) should be open.");
                if (!maze.HasWall(cx, cy, Wall.South))
                    Assert.AreEqual(0u, grid[(gy + 1) * gw + gx], $"South passage of ({cx},{cy}) should be open.");
            }
        }
    }
}
