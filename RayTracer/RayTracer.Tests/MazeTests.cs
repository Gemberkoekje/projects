using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class MazeTests
{
    // ────────────────────────────────────────────────────────────────
    //  1.  Basic construction
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Constructor_ValidDimensions_CreatesMaze()
    {
        var maze = new Maze(16, 16, seed: 42);

        Assert.AreEqual(16, maze.Width);
        Assert.AreEqual(16, maze.Height);
    }

    [TestMethod]
    public void Constructor_ZeroWidth_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Maze(0, 10));
    }

    [TestMethod]
    public void Constructor_ZeroHeight_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Maze(10, 0));
    }

    [TestMethod]
    public void Constructor_1x1_AllWallsIntact()
    {
        var maze = new Maze(1, 1, seed: 0);

        // A single-cell maze has nowhere to carve — all walls remain.
        Assert.IsTrue(maze.HasWall(0, 0, Wall.North));
        Assert.IsTrue(maze.HasWall(0, 0, Wall.South));
        Assert.IsTrue(maze.HasWall(0, 0, Wall.East));
        Assert.IsTrue(maze.HasWall(0, 0, Wall.West));
    }

    // ────────────────────────────────────────────────────────────────
    //  2.  Every cell reachable (flood-fill connectivity)
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(4, 4, 0, DisplayName = "4×4 seed 0")]
    [DataRow(16, 16, 42, DisplayName = "16×16 seed 42")]
    [DataRow(10, 20, 123, DisplayName = "10×20 seed 123")]
    [DataRow(1, 5, 7, DisplayName = "1×5 corridor")]
    [DataRow(5, 1, 99, DisplayName = "5×1 corridor")]
    public void FloodFill_ReachesEveryCell(int width, int height, int seed)
    {
        var maze = new Maze(width, height, seed);
        int reachable = FloodFillCount(maze, 0, 0);

        Assert.AreEqual(width * height, reachable,
            $"Flood-fill from (0,0) reached {reachable}/{width * height} cells.");
    }

    // ────────────────────────────────────────────────────────────────
    //  3.  Wall consistency — symmetric wall removal
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(8, 8, 0, DisplayName = "8×8 seed 0")]
    [DataRow(16, 16, 42, DisplayName = "16×16 seed 42")]
    [DataRow(3, 7, 55, DisplayName = "3×7 seed 55")]
    public void WallConsistency_SymmetricAcrossCells(int width, int height, int seed)
    {
        var maze = new Maze(width, height, seed);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // East / West pair
                if (x < width - 1)
                {
                    bool aEast = maze.HasWall(x, y, Wall.East);
                    bool bWest = maze.HasWall(x + 1, y, Wall.West);
                    Assert.AreEqual(aEast, bWest,
                        $"East wall of ({x},{y}) must match West wall of ({x + 1},{y}).");
                }

                // North / South pair
                if (y < height - 1)
                {
                    bool aSouth = maze.HasWall(x, y, Wall.South);
                    bool bNorth = maze.HasWall(x, y + 1, Wall.North);
                    Assert.AreEqual(aSouth, bNorth,
                        $"South wall of ({x},{y}) must match North wall of ({x},{y + 1}).");
                }
            }
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  4.  Perimeter walls — outer boundary must be solid
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(8, 8, 0, DisplayName = "8×8 seed 0")]
    [DataRow(16, 16, 42, DisplayName = "16×16 seed 42")]
    public void PerimeterWalls_AreIntact(int width, int height, int seed)
    {
        var maze = new Maze(width, height, seed);

        for (int x = 0; x < width; x++)
        {
            Assert.IsTrue(maze.HasWall(x, 0, Wall.North),
                $"Top-row cell ({x},0) must have North wall.");
            Assert.IsTrue(maze.HasWall(x, height - 1, Wall.South),
                $"Bottom-row cell ({x},{height - 1}) must have South wall.");
        }

        for (int y = 0; y < height; y++)
        {
            Assert.IsTrue(maze.HasWall(0, y, Wall.West),
                $"Left-column cell (0,{y}) must have West wall.");
            Assert.IsTrue(maze.HasWall(width - 1, y, Wall.East),
                $"Right-column cell ({width - 1},{y}) must have East wall.");
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  5.  Seed determinism
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SameSeed_ProducesIdenticalMaze()
    {
        var maze1 = new Maze(16, 16, seed: 42);
        var maze2 = new Maze(16, 16, seed: 42);

        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
                Assert.AreEqual(maze1[x, y].Walls, maze2[x, y].Walls,
                    $"Cell ({x},{y}) differs between identical-seed mazes.");
    }

    [TestMethod]
    public void DifferentSeed_ProducesDifferentMaze()
    {
        var maze1 = new Maze(16, 16, seed: 0);
        var maze2 = new Maze(16, 16, seed: 1);

        bool anyDifference = false;
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
                if (maze1[x, y].Walls != maze2[x, y].Walls)
                    anyDifference = true;

        Assert.IsTrue(anyDifference,
            "Two 16×16 mazes with different seeds should not be identical.");
    }

    // ────────────────────────────────────────────────────────────────
    //  6.  Perfect maze — exactly width*height - 1 passages
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(4, 4, 0, DisplayName = "4×4 seed 0")]
    [DataRow(16, 16, 42, DisplayName = "16×16 seed 42")]
    [DataRow(10, 20, 123, DisplayName = "10×20 seed 123")]
    public void PerfectMaze_HasExactlyNMinus1Passages(int width, int height, int seed)
    {
        var maze = new Maze(width, height, seed);
        int passages = CountPassages(maze);

        Assert.AreEqual(width * height - 1, passages,
            "A perfect maze must have exactly (W×H − 1) carved passages.");
    }

    // ────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Flood-fills from (startX, startY) respecting wall flags and returns
    /// the number of reachable cells.
    /// </summary>
    private static int FloodFillCount(Maze maze, int startX, int startY)
    {
        var visited = new bool[maze.Width, maze.Height];
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((startX, startY));
        visited[startX, startY] = true;
        int count = 0;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            count++;

            // North
            if (y > 0 && !maze.HasWall(x, y, Wall.North) && !visited[x, y - 1])
            {
                visited[x, y - 1] = true;
                queue.Enqueue((x, y - 1));
            }

            // South
            if (y < maze.Height - 1 && !maze.HasWall(x, y, Wall.South) && !visited[x, y + 1])
            {
                visited[x, y + 1] = true;
                queue.Enqueue((x, y + 1));
            }

            // East
            if (x < maze.Width - 1 && !maze.HasWall(x, y, Wall.East) && !visited[x + 1, y])
            {
                visited[x + 1, y] = true;
                queue.Enqueue((x + 1, y));
            }

            // West
            if (x > 0 && !maze.HasWall(x, y, Wall.West) && !visited[x - 1, y])
            {
                visited[x - 1, y] = true;
                queue.Enqueue((x - 1, y));
            }
        }

        return count;
    }

    /// <summary>
    /// Counts the total number of passages (removed interior wall pairs)
    /// by checking East and South walls for each cell. Each missing wall
    /// on an interior boundary counts as one passage.
    /// </summary>
    private static int CountPassages(Maze maze)
    {
        int passages = 0;

        for (int x = 0; x < maze.Width; x++)
        {
            for (int y = 0; y < maze.Height; y++)
            {
                // Only count East and South to avoid double-counting.
                if (x < maze.Width - 1 && !maze.HasWall(x, y, Wall.East))
                    passages++;
                if (y < maze.Height - 1 && !maze.HasWall(x, y, Wall.South))
                    passages++;
            }
        }

        return passages;
    }
}
