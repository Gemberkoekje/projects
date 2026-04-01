namespace RayTracer;

/// <summary>
/// Represents the wall flags for a maze cell. Each flag indicates the
/// presence of a wall on the corresponding side.
/// </summary>
[Flags]
public enum Wall
{
    None  = 0,
    North = 1,
    South = 2,
    East  = 4,
    West  = 8,
    All   = North | South | East | West,
}

/// <summary>
/// A single cell in the 2D maze grid. Stores which walls are present
/// and whether the cell has been visited during generation.
/// </summary>
public struct MazeCell
{
    public Wall Walls;
    public bool Visited;
}

/// <summary>
/// A 2D grid-based maze. Generates a perfect maze (exactly one path
/// between any two cells) using the recursive backtracker algorithm.
/// </summary>
public class Maze
{
    public int Width { get; }
    public int Height { get; }

    private readonly MazeCell[,] _cells;

    /// <summary>
    /// Creates a new maze with the specified dimensions and generates
    /// passages using the recursive backtracker (DFS) algorithm.
    /// </summary>
    /// <param name="width">Number of columns in the maze grid.</param>
    /// <param name="height">Number of rows in the maze grid.</param>
    /// <param name="seed">Random seed for reproducible generation.</param>
    public Maze(int width, int height, int seed = 0)
    {
        if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        _cells = new MazeCell[width, height];

        // Initialize every cell with all four walls.
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _cells[x, y].Walls = Wall.All;

        Generate(seed);
    }

    /// <summary>Returns the cell at the given grid coordinates.</summary>
    public ref MazeCell this[int x, int y] => ref _cells[x, y];

    /// <summary>
    /// Returns true if the cell at (x, y) has the specified wall flag set.
    /// </summary>
    public bool HasWall(int x, int y, Wall wall) => (_cells[x, y].Walls & wall) != 0;

    /// <summary>
    /// Generates the maze using the iterative recursive-backtracker (DFS)
    /// algorithm. This carves passages by removing wall pairs between
    /// adjacent cells, producing a perfect maze.
    /// </summary>
    private void Generate(int seed)
    {
        var rng = new Random(seed);
        var stack = new Stack<(int x, int y)>();

        // Start from (0, 0).
        _cells[0, 0].Visited = true;
        stack.Push((0, 0));

        // Pre-allocate a buffer for unvisited neighbors.
        Span<(int nx, int ny, Wall wall, Wall opposite)> neighbors = stackalloc (int, int, Wall, Wall)[4];

        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Peek();

            int count = GetUnvisitedNeighbors(cx, cy, neighbors);

            if (count == 0)
            {
                stack.Pop();
                continue;
            }

            // Pick a random unvisited neighbor.
            var (nx, ny, wall, opposite) = neighbors[rng.Next(count)];

            // Carve passage: remove the wall between the current cell and the neighbor.
            _cells[cx, cy].Walls &= ~wall;
            _cells[nx, ny].Walls &= ~opposite;

            _cells[nx, ny].Visited = true;
            stack.Push((nx, ny));
        }
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with unvisited neighbors of (x, y)
    /// and returns how many were found.
    /// </summary>
    private int GetUnvisitedNeighbors(int x, int y, Span<(int nx, int ny, Wall wall, Wall opposite)> buffer)
    {
        int count = 0;

        // North (y - 1)
        if (y > 0 && !_cells[x, y - 1].Visited)
            buffer[count++] = (x, y - 1, Wall.North, Wall.South);

        // South (y + 1)
        if (y < Height - 1 && !_cells[x, y + 1].Visited)
            buffer[count++] = (x, y + 1, Wall.South, Wall.North);

        // East (x + 1)
        if (x < Width - 1 && !_cells[x + 1, y].Visited)
            buffer[count++] = (x + 1, y, Wall.East, Wall.West);

        // West (x - 1)
        if (x > 0 && !_cells[x - 1, y].Visited)
            buffer[count++] = (x - 1, y, Wall.West, Wall.East);

        return count;
    }
}
