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
        float wallHeight = MazeGeometryBuilder.WallHeight)
    {
        var decals = new List<Tracable>();
        float cs = cellSize, wh = wallHeight;

        // OpenGL logo flat on the goal-cell floor — the end-of-maze payoff (plan §4) — and
        // on the start cell so the maze reads as "start … finish".
        if (opt.Logo)
        {
            AddFloorLogo(decals, placeholder, 0, 0, cs);
            AddFloorLogo(decals, placeholder, maze.Width - 1, maze.Height - 1, cs);
        }

        if (opt.Signs)
            AddWallSigns(decals, maze, placeholder, opt.Seed, cs, wh);

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

    private static void AddWallSigns(
        List<Tracable> decals, Maze maze, MaterialData mat, int seed, float cs, float wh)
    {
        var rng = new Random(seed);
        DecalLayer[] signs = [DecalLayer.SignExit, DecalLayer.SignArrow, DecalLayer.SignSmiley];

        // Decal spans the middle band of a wall's width and height.
        float sy = 0.28f * wh, h = 0.44f * wh;
        float w = 0.6f * cs;

        // Horizontal walls (normal ±Z): width runs +X, height +Y.
        for (int gy = 0; gy <= maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                bool hasWall = gy < maze.Height
                    ? maze.HasWall(gx, gy, Wall.North)
                    : maze.HasWall(gx, maze.Height - 1, Wall.South);
                if (!hasWall || rng.NextSingle() > 0.22f) continue;

                var origin = new Vector3((gx + 0.2f) * cs, sy, gy * cs);
                AddDoubleSided(decals, mat, origin, new Vector3(w, 0, 0), new Vector3(0, h, 0),
                    new Vector3(0, 0, 1), (int)signs[rng.Next(signs.Length)]);
            }
        }

        // Vertical walls (normal ±X): width runs +Z, height +Y.
        for (int gx = 0; gx <= maze.Width; gx++)
        {
            for (int gy = 0; gy < maze.Height; gy++)
            {
                bool hasWall = gx < maze.Width
                    ? maze.HasWall(gx, gy, Wall.West)
                    : maze.HasWall(maze.Width - 1, gy, Wall.East);
                if (!hasWall || rng.NextSingle() > 0.22f) continue;

                var origin = new Vector3(gx * cs, sy, (gy + 0.2f) * cs);
                AddDoubleSided(decals, mat, origin, new Vector3(0, 0, w), new Vector3(0, h, 0),
                    new Vector3(-1, 0, 0), (int)signs[rng.Next(signs.Length)]);
            }
        }
    }

    // Both maze-wall faces are walkable, so a sign is placed on both sides (offset ±eps
    // along the normal) with the back face's U axis flipped so its texture reads correctly.
    private static void AddDoubleSided(
        List<Tracable> decals, MaterialData mat, Vector3 origin, Vector3 uAxis, Vector3 vAxis, Vector3 normal, int layer)
    {
        Vector3 n = Vector3.Normalize(normal) * Eps;
        Vector3 f = origin + n;
        decals.Add(new DecalRectangle((f, f + uAxis, f + vAxis), mat, layer));
        Vector3 b = origin + uAxis - n;
        decals.Add(new DecalRectangle((b, b - uAxis, b + vAxis), mat, layer));
    }
}
