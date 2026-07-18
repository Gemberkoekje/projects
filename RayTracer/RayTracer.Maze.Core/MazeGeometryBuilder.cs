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

    // Whether a vertical wall exists on grid line gx at row gy (matches the vertical
    // wall loop's West/East convention). Used to decide where a thick wall needs a jamb.
    private static bool HasVWall(Maze maze, int gx, int gy)
    {
        if (gx < 0 || gx > maze.Width || gy < 0 || gy >= maze.Height) return false;
        return gx < maze.Width ? maze.HasWall(gx, gy, Wall.West) : maze.HasWall(maze.Width - 1, gy, Wall.East);
    }

    // Whether a horizontal wall exists on grid line gy at column gx (North/South convention).
    private static bool HasHWall(Maze maze, int gx, int gy)
    {
        if (gx < 0 || gx >= maze.Width || gy < 0 || gy > maze.Height) return false;
        return gy < maze.Height ? maze.HasWall(gx, gy, Wall.North) : maze.HasWall(gx, maze.Height - 1, Wall.South);
    }

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
    /// <param name="skipWall">Optional predicate <c>(gridX, gridY, horizontal) =&gt; skip</c>; when it
    /// returns true the wall at that grid line is omitted (e.g. so a window can replace it).</param>
    /// <param name="wallThickness">Wall thickness. 0 (default) builds zero-thickness single-quad walls
    /// (unchanged); &gt;0 builds each wall as two brick faces plus jamb side-walls at exposed ends.</param>
    public static Tracable[] Build(
        Maze maze,
        MaterialData wallMaterial,
        MaterialData floorMaterial,
        MaterialData ceilingMaterial,
        MaterialData? mortarMaterial = null,
        MaterialData? ceilingGridMaterial = null,
        MaterialData? goalMaterial = null,
        float cellSize = CellSize,
        float wallHeight = WallHeight,
        Func<int, int, bool, bool>? skipWall = null,
        float wallThickness = 0f,
        MaterialData? grassMaterial = null)
    {
        var tracables = new List<Tracable>();

        float cs = cellSize;
        float wh = wallHeight;
        float ht = wallThickness * 0.5f;

        // A brick wall panel (matches the maze wall look); a jamb is a plain-material
        // side wall closing a thick wall's exposed end.
        void AddPanel(Vector3 l1, Vector3 l2, Vector3 l3) =>
            tracables.Add(mortarMaterial is not null
                ? new BrickRectangle((l1, l2, l3), wallMaterial, mortarMaterial)
                : new TracableRectangle((l1, l2, l3), wallMaterial));
        void AddJamb(Vector3 l1, Vector3 l2, Vector3 l3) =>
            tracables.Add(new TracableRectangle((l1, l2, l3), wallMaterial));

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

                if (hasWall && skipWall?.Invoke(gx, gy, true) != true)
                {
                    float z = gy * cs;
                    float x0 = gx * cs, x1 = (gx + 1) * cs;
                    if (ht <= 0f)
                    {
                        var loc = (new Vector3(x0, 0, z), new Vector3(x1, 0, z), new Vector3(x0, wh, z));
                        tracables.Add(mortarMaterial is not null
                            ? new BrickRectangle(loc, wallMaterial, mortarMaterial)
                            : new TracableRectangle(loc, wallMaterial));
                    }
                    else
                    {
                        // Two brick faces a thickness apart, and a jamb closing each end
                        // that no collinear wall continues (a 180° corner / opening).
                        AddPanel(new Vector3(x0, 0, z - ht), new Vector3(x1, 0, z - ht), new Vector3(x0, wh, z - ht));
                        AddPanel(new Vector3(x0, 0, z + ht), new Vector3(x1, 0, z + ht), new Vector3(x0, wh, z + ht));
                        if (!HasHWall(maze, gx - 1, gy))
                            AddJamb(new Vector3(x0, 0, z - ht), new Vector3(x0, 0, z + ht), new Vector3(x0, wh, z - ht));
                        if (!HasHWall(maze, gx + 1, gy))
                            AddJamb(new Vector3(x1, 0, z - ht), new Vector3(x1, 0, z + ht), new Vector3(x1, wh, z - ht));
                    }
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

                if (hasWall && skipWall?.Invoke(gx, gy, false) != true)
                {
                    float x = gx * cs;
                    float z0 = gy * cs, z1 = (gy + 1) * cs;
                    if (ht <= 0f)
                    {
                        var loc = (new Vector3(x, 0, z0), new Vector3(x, 0, z1), new Vector3(x, wh, z0));
                        tracables.Add(mortarMaterial is not null
                            ? new BrickRectangle(loc, wallMaterial, mortarMaterial)
                            : new TracableRectangle(loc, wallMaterial));
                    }
                    else
                    {
                        AddPanel(new Vector3(x - ht, 0, z0), new Vector3(x - ht, 0, z1), new Vector3(x - ht, wh, z0));
                        AddPanel(new Vector3(x + ht, 0, z0), new Vector3(x + ht, 0, z1), new Vector3(x + ht, wh, z0));
                        if (!HasVWall(maze, gx, gy - 1))
                            AddJamb(new Vector3(x - ht, 0, z0), new Vector3(x + ht, 0, z0), new Vector3(x - ht, wh, z0));
                        if (!HasVWall(maze, gx, gy + 1))
                            AddJamb(new Vector3(x - ht, 0, z1), new Vector3(x + ht, 0, z1), new Vector3(x - ht, wh, z1));
                    }
                }
            }
        }

        // ── Floor (y = 0) ───────────────────────────────────────────
        float totalX = maze.Width * cs;
        float totalZ = maze.Height * cs;

        if (maze.HasOutdoor)
        {
            // Per-cell floor so outdoor cells can be grass (outdoor-area-plan.md §O5); indoor keeps stone.
            for (int gx = 0; gx < maze.Width; gx++)
                for (int gy = 0; gy < maze.Height; gy++)
                {
                    MaterialData mat = maze.IsOutdoor(gx, gy) && grassMaterial is not null ? grassMaterial : floorMaterial;
                    float x0 = gx * cs, x1 = (gx + 1) * cs, z0 = gy * cs, z1 = (gy + 1) * cs;
                    tracables.Add(new TracableRectangle(
                        (new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x0, 0, z1)), mat));
                }
        }
        else
        {
            tracables.Add(new TracableRectangle(
                (new Vector3(0, 0, 0),
                 new Vector3(totalX, 0, 0),
                 new Vector3(0, 0, totalZ)),
                floorMaterial));
        }

        // ── Ceiling (y = wallHeight) ────────────────────────────────
        if (maze.HasOutdoor)
        {
            // Open-roof half (outdoor-area-plan.md §O1): emit the ceiling per-cell so outdoor cells are
            // left open to the sky. 4 tiles/cell keeps the pattern aligned with the classic full ceiling.
            for (int gx = 0; gx < maze.Width; gx++)
                for (int gy = 0; gy < maze.Height; gy++)
                {
                    if (maze.IsOutdoor(gx, gy))
                        continue; // no ceiling — rays escape to the sky
                    float x0 = gx * cs, x1 = (gx + 1) * cs, z0 = gy * cs, z1 = (gy + 1) * cs;
                    var tileLoc = (new Vector3(x0, wh, z0), new Vector3(x1, wh, z0), new Vector3(x0, wh, z1));
                    tracables.Add(ceilingGridMaterial is not null
                        ? new CeilingTileRectangle(tileLoc, ceilingMaterial, ceilingGridMaterial, 4f, 4f)
                        : new TracableRectangle(tileLoc, ceilingMaterial));
                }
        }
        else
        {
            // Classic roofed maze: one ceiling rectangle over the whole grid (unchanged — bit-identical).
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
    /// Creates point lights and wall torches for the maze based on cell
    /// properties. Some blocks have ceiling lights, others are unlit, and
    /// designated biome regions have wall-mounted torches instead.
    /// </summary>
    /// <param name="maze">The maze to create lights for.</param>
    /// <param name="cellSize">World-space size of each cell.</param>
    /// <param name="wallHeight">Wall height.</param>
    /// <param name="lightSpawnChance">Probability (0-1) that a cell gets a ceiling light.</param>
    /// <param name="biomeSize">Size of torch biome regions (0 to disable biomes).</param>
    /// <param name="seed">Seed for random light placement.</param>
    public static Light[] BuildLights(
        Maze maze,
        float cellSize = CellSize,
        float wallHeight = WallHeight,
        float lightSpawnChance = 0.4f,
        int biomeSize = 4,
        int seed = 0,
        float emissionTempK = 0f) // §O9: blackbody temp for the maze torches/ceiling lights (0 = flat white)
    {
        var lights = new List<Light>();
        var rng = new Random(seed);

        for (int gy = 0; gy < maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                // §O8: outdoor cells are open-air — no wall torches or ceiling lights (there is no ceiling).
                // They are lit by the sun (day) and the sky (dim at night), so the garden goes dark at night
                // instead of being lit by point lights that ignore the day/night cycle. Indoor-only mazes
                // (no outdoor region) skip nothing, so their light placement is unchanged.
                if (maze.IsOutdoor(gx, gy)) continue;

                // Determine which biome this cell belongs to
                int biomeX = biomeSize > 0 ? gx / biomeSize : -1;
                int biomeY = biomeSize > 0 ? gy / biomeSize : -1;
                bool isTorchBiome = (biomeX + biomeY) % 2 == 0;

                if (biomeSize > 0 && isTorchBiome)
                {
                    // Place wall torches in this biome region
                    AddWallTorches(lights, maze, gx, gy, cellSize, wallHeight, rng, emissionTempK);
                }
                else if (rng.NextSingle() < lightSpawnChance)
                {
                    // Place a ceiling light in this cell
                    lights.Add(new Light
                    {
                        Position = new Vector3(
                            (gx + 0.5f) * cellSize,
                            wallHeight - 0.05f,
                            (gy + 0.5f) * cellSize),
                        Color = new Vector3(1f, 1f, 1f),
                        Ambient = 0f,
                        EmissionTempK = emissionTempK
                    });
                }
            }
        }
        return lights.ToArray();
    }

    /// <summary>
    /// Adds wall-mounted torches to the perimeter walls of a cell
    /// that faces adjacent unlit regions. Torches are dimmer than ceiling
    /// lights and their color varies slightly to simulate flickering flames.
    /// </summary>
    private static void AddWallTorches(
        List<Light> lights,
        Maze maze,
        int cellX,
        int cellY,
        float cellSize,
        float wallHeight,
        Random rng,
        float emissionTempK)
    {
        float torchHeight = wallHeight * 0.75f;

        // Consistent warm torch color (no per-frame variation)
        var torchColor = new Vector3(1f, 0.75f, 0.5f);

        // Check each wall direction
        // North wall
        if (cellY > 0 && !maze.HasWall(cellX, cellY - 1, Wall.South))
        {
            float z = cellY * cellSize;
            lights.Add(new Light
            {
                Position = new Vector3((cellX + 0.5f) * cellSize, torchHeight, z + 0.1f),
                Color = torchColor,
                Ambient = 0f,
                EmissionTempK = emissionTempK
            });
        }

        // South wall
        if (cellY < maze.Height - 1 && !maze.HasWall(cellX, cellY + 1, Wall.North))
        {
            float z = (cellY + 1) * cellSize;
            lights.Add(new Light
            {
                Position = new Vector3((cellX + 0.5f) * cellSize, torchHeight, z - 0.1f),
                Color = torchColor,
                Ambient = 0f,
                EmissionTempK = emissionTempK
            });
        }

        // West wall
        if (cellX > 0 && !maze.HasWall(cellX - 1, cellY, Wall.East))
        {
            float x = cellX * cellSize;
            lights.Add(new Light
            {
                Position = new Vector3(x + 0.1f, torchHeight, (cellY + 0.5f) * cellSize),
                Color = torchColor,
                Ambient = 0f,
                EmissionTempK = emissionTempK
            });
        }

        // East wall
        if (cellX < maze.Width - 1 && !maze.HasWall(cellX + 1, cellY, Wall.West))
        {
            float x = (cellX + 1) * cellSize;
            lights.Add(new Light
            {
                Position = new Vector3(x - 0.1f, torchHeight, (cellY + 0.5f) * cellSize),
                Color = torchColor,
                Ambient = 0f,
                EmissionTempK = emissionTempK
            });
        }
    }
}
