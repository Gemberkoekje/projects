using System.Numerics;

namespace RayTracer;

/// <summary>
/// Converts a 2D <see cref="Maze"/> into an array of <see cref="Tracable"/>
/// quads (walls, floor, ceiling) suitable for rendering.
/// </summary>
public static class MazeGeometryBuilder
{
    /// <summary>World-space size of each maze cell (width and depth).</summary>
    public const float CellSize = 2f;

    /// <summary>Height of the maze walls.</summary>
    public const float WallHeight = 2f;

    /// <summary>
    /// Builds the 3D geometry for the given maze. Each cell becomes a
    /// <paramref name="cellSize"/>×<paramref name="cellSize"/> square in
    /// the XZ plane. Walls are vertical rectangles of the given
    /// <paramref name="wallHeight"/>. Floor is at Y=0 and ceiling at
    /// Y=<paramref name="wallHeight"/>.
    /// </summary>
    /// <param name="maze">The maze to convert.</param>
    /// <param name="wallMaterial">Spectral material for wall quads (brick face).</param>
    /// <param name="floorMaterial">Spectral material for the floor.</param>
    /// <param name="ceilingMaterial">Spectral material for the ceiling tiles.</param>
    /// <param name="mortarMaterial">Material for mortar between bricks. When null, walls use a plain rectangle.</param>
    /// <param name="ceilingGridMaterial">Material for ceiling grid lines. When null, ceiling uses a plain rectangle.</param>
    /// <param name="cellSize">World-space size of each cell. Defaults to <see cref="CellSize"/>.</param>
    /// <param name="wallHeight">Wall height. Defaults to <see cref="WallHeight"/>.</param>
    public static Tracable[] Build(
        Maze maze,
        MaterialData wallMaterial,
        MaterialData floorMaterial,
        MaterialData ceilingMaterial,
        MaterialData? mortarMaterial = null,
        MaterialData? ceilingGridMaterial = null,
        MaterialData? goalMaterial = null,
        float cellSize = CellSize,
        float wallHeight = WallHeight)
    {
        var tracables = new List<Tracable>();

        float cs = cellSize;
        float wh = wallHeight;

        // ── Horizontal walls (perpendicular to Z axis) ──────────────
        // Walk every horizontal grid line from y=0 to y=maze.Height.
        // To avoid duplicates: use the North flag for lines 0..Height-1
        // and the South flag of the last row for line Height.
        for (int gy = 0; gy <= maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                bool hasWall = gy < maze.Height
                    ? maze.HasWall(gx, gy, Wall.North)
                    : maze.HasWall(gx, maze.Height - 1, Wall.South);

                if (hasWall)
                {
                    float z = gy * cs;
                    var loc = (new Vector3(gx * cs, 0, z),
                               new Vector3((gx + 1) * cs, 0, z),
                               new Vector3(gx * cs, wh, z));
                    tracables.Add(mortarMaterial is not null
                        ? new BrickRectangle(loc, wallMaterial, mortarMaterial)
                        : new TracableRectangle(loc, wallMaterial));
                }
            }
        }

        // ── Vertical walls (perpendicular to X axis) ────────────────
        // Walk every vertical grid line from x=0 to x=maze.Width.
        for (int gx = 0; gx <= maze.Width; gx++)
        {
            for (int gy = 0; gy < maze.Height; gy++)
            {
                bool hasWall = gx < maze.Width
                    ? maze.HasWall(gx, gy, Wall.West)
                    : maze.HasWall(maze.Width - 1, gy, Wall.East);

                if (hasWall)
                {
                    float x = gx * cs;
                    var loc = (new Vector3(x, 0, gy * cs),
                               new Vector3(x, 0, (gy + 1) * cs),
                               new Vector3(x, wh, gy * cs));
                    tracables.Add(mortarMaterial is not null
                        ? new BrickRectangle(loc, wallMaterial, mortarMaterial)
                        : new TracableRectangle(loc, wallMaterial));
                }
            }
        }

        // ── Floor (y = 0) ───────────────────────────────────────────
        float totalX = maze.Width * cs;
        float totalZ = maze.Height * cs;

        tracables.Add(new TracableRectangle(
            (new Vector3(0, 0, 0),
             new Vector3(totalX, 0, 0),
             new Vector3(0, 0, totalZ)),
            floorMaterial));

        // ── Ceiling (y = wallHeight) ────────────────────────────────
        var ceilingLoc = (new Vector3(0, wh, 0),
                          new Vector3(totalX, wh, 0),
                          new Vector3(0, wh, totalZ));
        if (ceilingGridMaterial is not null)
        {
            // 4 tiles per cell in each direction
            float tilesAcross = maze.Width * 4f;
            float tilesDown = maze.Height * 4f;
            tracables.Add(new CeilingTileRectangle(
                ceilingLoc, ceilingMaterial, ceilingGridMaterial,
                tilesAcross, tilesDown));
        }
        else
        {
            tracables.Add(new TracableRectangle(ceilingLoc, ceilingMaterial));
        }

        // ── Goal marker (colored floor patch at the exit cell) ──────
        if (goalMaterial is not null)
        {
            int gx = maze.Width - 1;
            int gy = maze.Height - 1;
            float eps = 0.001f; // tiny offset to avoid z-fighting with floor
            tracables.Add(new TracableRectangle(
                (new Vector3(gx * cs, eps, gy * cs),
                 new Vector3((gx + 1) * cs, eps, gy * cs),
                 new Vector3(gx * cs, eps, (gy + 1) * cs)),
                goalMaterial));
        }

        return tracables.ToArray();
    }

    /// <summary>
    /// Creates a point light at the ceiling centre of every maze cell.
    /// Lights are placed slightly below the ceiling to avoid
    /// self-intersection with the ceiling plane.
    /// </summary>
    public static Light[] BuildLights(
        Maze maze,
        float cellSize = CellSize,
        float wallHeight = WallHeight)
    {
        var lights = new List<Light>();
        for (int gy = 0; gy < maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                lights.Add(new Light
                {
                    Position = new Vector3(
                        (gx + 0.5f) * cellSize,
                        wallHeight - 0.05f,
                        (gy + 0.5f) * cellSize),
                    Color = new Vector3(1f, 1f, 1f),
                    Ambient = 0.05f
                });
            }
        }
        return lights.ToArray();
    }
}
