using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class MazeNavigatorTests
{
    // ── Direction helpers ───────────────────────────────────────────

    [TestMethod]
    public void TurnRight_North_ReturnsEast()
        => Assert.AreEqual(Direction.East, MazeNavigator.TurnRight(Direction.North));

    [TestMethod]
    public void TurnRight_East_ReturnsSouth()
        => Assert.AreEqual(Direction.South, MazeNavigator.TurnRight(Direction.East));

    [TestMethod]
    public void TurnRight_South_ReturnsWest()
        => Assert.AreEqual(Direction.West, MazeNavigator.TurnRight(Direction.South));

    [TestMethod]
    public void TurnRight_West_ReturnsNorth()
        => Assert.AreEqual(Direction.North, MazeNavigator.TurnRight(Direction.West));

    [TestMethod]
    public void TurnLeft_North_ReturnsWest()
        => Assert.AreEqual(Direction.West, MazeNavigator.TurnLeft(Direction.North));

    [TestMethod]
    public void Reverse_North_ReturnsSouth()
        => Assert.AreEqual(Direction.South, MazeNavigator.Reverse(Direction.North));

    [TestMethod]
    public void Reverse_East_ReturnsWest()
        => Assert.AreEqual(Direction.West, MazeNavigator.Reverse(Direction.East));

    // ── Wall mapping ───────────────────────────────────────────────

    [TestMethod]
    public void ToWall_MapsCorrectly()
    {
        Assert.AreEqual(Wall.North, MazeNavigator.ToWall(Direction.North));
        Assert.AreEqual(Wall.South, MazeNavigator.ToWall(Direction.South));
        Assert.AreEqual(Wall.East, MazeNavigator.ToWall(Direction.East));
        Assert.AreEqual(Wall.West, MazeNavigator.ToWall(Direction.West));
    }

    [TestMethod]
    public void ToOffset_MapsCorrectly()
    {
        Assert.AreEqual((0, -1), MazeNavigator.ToOffset(Direction.North));
        Assert.AreEqual((0, 1), MazeNavigator.ToOffset(Direction.South));
        Assert.AreEqual((1, 0), MazeNavigator.ToOffset(Direction.East));
        Assert.AreEqual((-1, 0), MazeNavigator.ToOffset(Direction.West));
    }

    // ── Navigation ─────────────────────────────────────────────────

    [TestMethod]
    public void Advance_MovesNavigatorToNextCell()
    {
        var maze = new Maze(4, 4, seed: 0);
        var nav = new MazeNavigator(maze, 0, 0, Direction.South);

        var (nx, ny, nh) = nav.PeekNext();
        nav.Advance();

        Assert.AreEqual(nx, nav.CellX);
        Assert.AreEqual(ny, nav.CellY);
        Assert.AreEqual(nh, nav.Heading);
    }

    [TestMethod]
    public void PeekNext_DoesNotMutateState()
    {
        var maze = new Maze(4, 4, seed: 0);
        var nav = new MazeNavigator(maze, 0, 0, Direction.South);

        int x0 = nav.CellX, y0 = nav.CellY;
        Direction h0 = nav.Heading;

        _ = nav.PeekNext();

        Assert.AreEqual(x0, nav.CellX);
        Assert.AreEqual(y0, nav.CellY);
        Assert.AreEqual(h0, nav.Heading);
    }

    [TestMethod]
    [DataRow(4, 4, 0, DisplayName = "4×4 seed 0")]
    [DataRow(8, 8, 42, DisplayName = "8×8 seed 42")]
    [DataRow(16, 16, 42, DisplayName = "16×16 seed 42")]
    public void WalkDoesNotGetStuck(int w, int h, int seed)
    {
        var maze = new Maze(w, h, seed);
        var nav = new MazeNavigator(maze, 0, 0, Direction.South);

        // Walk a large number of steps; should never throw or stall.
        int totalCells = w * h;
        var visited = new HashSet<(int, int)>();
        for (int step = 0; step < totalCells * 4; step++)
        {
            visited.Add((nav.CellX, nav.CellY));
            nav.Advance();
        }

        // The right-hand rule should visit every cell in a perfect maze.
        Assert.AreEqual(totalCells, visited.Count,
            $"Visited {visited.Count}/{totalCells} cells.");
    }

    [TestMethod]
    public void PeekNext_NextCellIsAdjacentOrSame()
    {
        var maze = new Maze(8, 8, seed: 42);
        var nav = new MazeNavigator(maze, 0, 0, Direction.South);

        for (int i = 0; i < 100; i++)
        {
            var (nx, ny, _) = nav.PeekNext();
            int dx = Math.Abs(nx - nav.CellX);
            int dy = Math.Abs(ny - nav.CellY);
            Assert.IsTrue(dx + dy <= 1, $"Step {i}: jumped from ({nav.CellX},{nav.CellY}) to ({nx},{ny}).");
            nav.Advance();
        }
    }
}
