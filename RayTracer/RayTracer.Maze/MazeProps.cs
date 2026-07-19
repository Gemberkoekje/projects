using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Places the classic prop decals (plan §4–§5) into a maze: the OpenGL logo on the goal
/// (and start) cell floor, and wall signs on a deterministic subset of walls. Each prop is
/// a <see cref="DecalRectangle"/> carrying an atlas layer index; the GPU shades it from the
/// decal atlas through the RGB→reflectance basis. Placement is seeded so a run is
/// reproducible.
/// </summary>
internal static class MazeProps
{
    internal sealed record Options(bool Logo = true, bool Signs = true, int Seed = 0);

    private const float Eps = 0.012f; // offset off the surface to avoid z-fighting

    internal static List<Tracable> Build(
        Maze maze, MaterialData placeholder, Options opt,
        float cellSize = MazeGeometryBuilder.CellSize,
        float wallHeight = MazeGeometryBuilder.WallHeight,
        float wallThickness = 0f,
        IReadOnlySet<(int, int, bool)>? excludeWalls = null)
    {
        var decals = new List<Tracable>();
        float cs = cellSize, wh = wallHeight;

        // OpenGL logo flat on the goal-cell floor — the end-of-maze payoff (plan §4) — and
        // on the start cell so the maze reads as "start … finish".
        if (opt.Logo)
        {
            AddFloorLogo(decals, placeholder, 0, 0, cs); // start (row 0 is always Indoor)
            // Goal logo only when the goal cell is Indoor — §O keeps the logo out of the open garden.
            if (!maze.IsOutdoor(maze.Width - 1, maze.Height - 1))
                AddFloorLogo(decals, placeholder, maze.Width - 1, maze.Height - 1, cs);
        }

        if (opt.Signs)
            AddWallSigns(decals, maze, placeholder, opt.Seed, cs, wh, wallThickness, excludeWalls);

        return decals;
    }

    private static void AddFloorLogo(List<Tracable> decals, MaterialData mat, int cx, int cy, float cs)
    {
        float x0 = (cx + 0.15f) * cs, x1 = (cx + 0.85f) * cs;
        float z0 = (cy + 0.15f) * cs, z1 = (cy + 0.85f) * cs;
        // Facing up: edge1 = +X, edge2 = +Z.
        decals.Add(new DecalRectangle(
            (new Vector3(x0, Eps, z0), new Vector3(x1, Eps, z0), new Vector3(x0, Eps, z1)),
            mat, (int)DecalLayer.OpenGlLogo));
    }

    private static readonly DecalLayer[] SignLayers = [DecalLayer.SignExit, DecalLayer.SignArrow, DecalLayer.SignSmiley];

    /// <summary>
    /// Deterministic wall-sign selection (the RNG order matches <see cref="AddWallSigns"/>
    /// exactly, so placement is unchanged). Both the geometry builder and
    /// <see cref="SignWalls"/> use this, so mirrors/windows can avoid sign walls.
    /// </summary>
    private static List<(int gx, int gy, bool horizontal, DecalLayer layer)> SelectSigns(
        Maze maze, int seed, IReadOnlySet<(int, int, bool)>? excludeWalls = null)
    {
        var result = new List<(int, int, bool, DecalLayer)>();
        var rng = new Random(seed);

        for (int gy = 0; gy <= maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                bool hasWall = gy < maze.Height
                    ? maze.HasWall(gx, gy, Wall.North)
                    : maze.HasWall(gx, maze.Height - 1, Wall.South);
                if (!hasWall || rng.NextSingle() > 0.22f) continue;
                DecalLayer layer = SignLayers[rng.Next(SignLayers.Length)]; // draw regardless so the RNG stream is stable
                if (excludeWalls is null || !excludeWalls.Contains((gx, gy, true)))
                    result.Add((gx, gy, true, layer));
            }
        }

        for (int gx = 0; gx <= maze.Width; gx++)
        {
            for (int gy = 0; gy < maze.Height; gy++)
            {
                bool hasWall = gx < maze.Width
                    ? maze.HasWall(gx, gy, Wall.West)
                    : maze.HasWall(maze.Width - 1, gy, Wall.East);
                if (!hasWall || rng.NextSingle() > 0.22f) continue;
                DecalLayer layer = SignLayers[rng.Next(SignLayers.Length)];
                if (excludeWalls is null || !excludeWalls.Contains((gx, gy, false)))
                    result.Add((gx, gy, false, layer));
            }
        }

        return result;
    }

    /// <summary>The set of walls (grid key <c>(gx, gy, horizontal)</c>) that carry a wall
    /// sign, so mirrors and windows can steer clear of them. Empty when signs are off.
    /// <paramref name="excludeWalls"/> (e.g. the outdoor hedge walls) get no sign — §O keeps indoor decor
    /// out of the open garden while still allowing it on the boundary.</summary>
    internal static HashSet<(int, int, bool)> SignWalls(Maze maze, Options opt, IReadOnlySet<(int, int, bool)>? excludeWalls = null)
    {
        var set = new HashSet<(int, int, bool)>();
        if (opt.Signs)
            foreach (var (gx, gy, horizontal, _) in SelectSigns(maze, opt.Seed, excludeWalls))
                set.Add((gx, gy, horizontal));
        return set;
    }

    private static void AddWallSigns(
        List<Tracable> decals, Maze maze, MaterialData mat, int seed, float cs, float wh, float wallThickness,
        IReadOnlySet<(int, int, bool)>? excludeWalls)
    {
        // Decal spans the middle band of a wall's width and height.
        float sy = 0.28f * wh, h = 0.44f * wh;
        float w = 0.6f * cs;
        // Mount on the wall's visible faces, not the centre plane: a wallThickness > 0 wall is built as
        // two faces at ±wallThickness/2 (MazeGeometryBuilder), so the sign must clear that half-thickness
        // or it ends up buried inside the slab (the same faceOffset MazeMirrors/MazeWindows apply).
        float faceOffset = wallThickness * 0.5f;

        foreach (var (gx, gy, horizontal, layer) in SelectSigns(maze, seed, excludeWalls))
        {
            if (horizontal) // normal ±Z: width runs +X, height +Y
            {
                var origin = new Vector3((gx + 0.2f) * cs, sy, gy * cs);
                AddDoubleSided(decals, mat, origin, new Vector3(w, 0, 0), new Vector3(0, h, 0),
                    new Vector3(0, 0, 1), (int)layer, faceOffset);
            }
            else // normal ±X: width runs +Z, height +Y
            {
                var origin = new Vector3(gx * cs, sy, (gy + 0.2f) * cs);
                AddDoubleSided(decals, mat, origin, new Vector3(0, 0, w), new Vector3(0, h, 0),
                    new Vector3(-1, 0, 0), (int)layer, faceOffset);
            }
        }
    }

    // Both maze-wall faces are walkable, so a sign is placed on both sides, each sitting just outside its
    // wall face — offset along the normal by the wall half-thickness plus a small epsilon (z-fighting) —
    // with the back face's U axis flipped so its texture reads correctly.
    private static void AddDoubleSided(
        List<Tracable> decals, MaterialData mat, Vector3 origin, Vector3 uAxis, Vector3 vAxis, Vector3 normal, int layer, float faceOffset)
    {
        Vector3 n = Vector3.Normalize(normal) * (faceOffset + Eps);
        Vector3 f = origin + n;
        decals.Add(new DecalRectangle((f, f + uAxis, f + vAxis), mat, layer));
        Vector3 b = origin + uAxis - n;
        decals.Add(new DecalRectangle((b, b - uAxis, b + vAxis), mat, layer));
    }
}
