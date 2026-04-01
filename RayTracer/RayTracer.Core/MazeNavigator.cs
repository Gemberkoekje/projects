namespace RayTracer;

/// <summary>
/// Cardinal heading directions for the camera within the maze.
/// North = -Z, South = +Z, East = +X, West = -X in world space.
/// </summary>
public enum Direction
{
    North,
    East,
    South,
    West,
}

/// <summary>
/// Autonomous maze walker that uses the right-hand wall-following rule
/// to decide the next cell and heading at every step.
/// </summary>
public class MazeNavigator
{
    private readonly Maze _maze;

    public int CellX { get; set; }
    public int CellY { get; set; }
    public Direction Heading { get; set; }

    public MazeNavigator(Maze maze, int startX, int startY, Direction startHeading)
    {
        _maze = maze;
        CellX = startX;
        CellY = startY;
        Heading = startHeading;
    }

    /// <summary>
    /// Determines the next cell and heading using the right-hand
    /// wall-following rule: try right, straight, left, then back.
    /// </summary>
    public (int nextX, int nextY, Direction nextHeading) PeekNext()
    {
        // Priority order: right turn, straight, left turn, U-turn.
        ReadOnlySpan<Direction> priorities =
        [
            TurnRight(Heading),
            Heading,
            TurnLeft(Heading),
            Reverse(Heading),
        ];

        foreach (var dir in priorities)
        {
            if (!_maze.HasWall(CellX, CellY, ToWall(dir)))
            {
                var (dx, dy) = ToOffset(dir);
                return (CellX + dx, CellY + dy, dir);
            }
        }

        // Dead-end surrounded by four walls (should not happen in a valid maze).
        return (CellX, CellY, Heading);
    }

    /// <summary>Advances one step using the right-hand rule.</summary>
    public void Advance()
    {
        var (nx, ny, nh) = PeekNext();
        CellX = nx;
        CellY = ny;
        Heading = nh;
    }

    // ── Direction helpers ──────────────────────────────────────────

    public static Direction TurnRight(Direction d) => d switch
    {
        Direction.North => Direction.East,
        Direction.East  => Direction.South,
        Direction.South => Direction.West,
        Direction.West  => Direction.North,
        _ => d,
    };

    public static Direction TurnLeft(Direction d) => d switch
    {
        Direction.North => Direction.West,
        Direction.West  => Direction.South,
        Direction.South => Direction.East,
        Direction.East  => Direction.North,
        _ => d,
    };

    public static Direction Reverse(Direction d) => d switch
    {
        Direction.North => Direction.South,
        Direction.South => Direction.North,
        Direction.East  => Direction.West,
        Direction.West  => Direction.East,
        _ => d,
    };

    public static Wall ToWall(Direction d) => d switch
    {
        Direction.North => Wall.North,
        Direction.South => Wall.South,
        Direction.East  => Wall.East,
        Direction.West  => Wall.West,
        _ => Wall.None,
    };

    public static (int dx, int dy) ToOffset(Direction d) => d switch
    {
        Direction.North => (0, -1),
        Direction.South => (0,  1),
        Direction.East  => (1,  0),
        Direction.West  => (-1, 0),
        _ => (0, 0),
    };
}
