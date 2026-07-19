using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Places framed mirrors (spectral-effects-plan.md §1.1) on a seeded subset of maze
/// walls. Each mirror is a <see cref="SurfaceKind.Mirror"/> quad flush on a wall,
/// facing into an adjacent open cell, with a dark diffuse frame quad just behind it
/// so a border reads around the reflective panel. The wall behind occludes the back,
/// so one quad per placement suffices (the shader orients the reflection to whichever
/// side the viewer is on, and the wall hides the far side).
///
/// The mirror material is a neutral silver (near-flat high reflectance); a future
/// step can swap in measured gold/silver curves. Placement is seeded so a run is
/// reproducible, mirroring <see cref="MazeProps"/>.
/// </summary>
internal static class MazeMirrors
{
    /// <param name="Chance">Probability a given wall receives a mirror.</param>
    /// <param name="Seed">Seed for reproducible placement.</param>
    internal sealed record Options(float Chance = 0.12f, int Seed = 0);

    // Built once: a neutral silver mirror and a dark frame. Distinct ids so the GPU
    // scene packer (which de-dupes materials by Id) keeps them separate.
    private static readonly MaterialData MirrorMat = new(
        "mirror-wall", "Mirror", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        FlatSpectrum(0.90f), SurfaceKind.Mirror);

    private static readonly MaterialData FrameMat = new(
        "mirror-frame", "Frame", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.5f,
        FlatSpectrum(0.045f), SurfaceKind.Diffuse);

    /// <summary>
    /// Selects the walls that get a mirror (grid key <c>(gx, gy, horizontal)</c>):
    /// existing walls on a seeded subset, minus any wall carrying a sign
    /// (<paramref name="excludeDecals"/>) so a mirror never shares a wall with a decal.
    /// </summary>
    internal static HashSet<(int, int, bool)> SelectWalls(Maze maze, Options opt, ISet<(int, int, bool)> excludeDecals)
    {
        var result = new HashSet<(int, int, bool)>();
        var rng = new Random(opt.Seed);

        for (int gy = 0; gy <= maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                bool hasWall = gy < maze.Height
                    ? maze.HasWall(gx, gy, Wall.North)
                    : maze.HasWall(gx, maze.Height - 1, Wall.South);
                if (!hasWall || rng.NextSingle() > opt.Chance) continue;
                if (!excludeDecals.Contains((gx, gy, true))) result.Add((gx, gy, true));
            }
        }

        for (int gx = 0; gx <= maze.Width; gx++)
        {
            for (int gy = 0; gy < maze.Height; gy++)
            {
                bool hasWall = gx < maze.Width
                    ? maze.HasWall(gx, gy, Wall.West)
                    : maze.HasWall(maze.Width - 1, gy, Wall.East);
                if (!hasWall || rng.NextSingle() > opt.Chance) continue;
                if (!excludeDecals.Contains((gx, gy, false))) result.Add((gx, gy, false));
            }
        }

        return result;
    }

    internal static List<Tracable> Build(
        Maze maze, ISet<(int, int, bool)> mirrorWalls,
        float cellSize = MazeGeometryBuilder.CellSize,
        float wallHeight = MazeGeometryBuilder.WallHeight,
        float wallThickness = 0f)
    {
        var result = new List<Tracable>();
        float cs = cellSize, wh = wallHeight;
        float faceOffset = wallThickness * 0.5f; // mount on the wall face, not the centre plane

        // Horizontal walls (normal ±Z). Face into cell (gx, gy) for interior lines;
        // on the far boundary line (gy == Height) face back into (gx, Height-1).
        for (int gy = 0; gy <= maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                if (!mirrorWalls.Contains((gx, gy, true))) continue;
                Vector3 normal = gy < maze.Height ? new Vector3(0, 0, 1) : new Vector3(0, 0, -1);
                Vector3 wallCenter = new((gx + 0.5f) * cs, wh * 0.5f, gy * cs);
                AddFramedMirror(result, wallCenter + normal * faceOffset, normal, cs, wh);
            }
        }

        // Vertical walls (normal ±X).
        for (int gx = 0; gx <= maze.Width; gx++)
        {
            for (int gy = 0; gy < maze.Height; gy++)
            {
                if (!mirrorWalls.Contains((gx, gy, false))) continue;
                Vector3 normal = gx < maze.Width ? new Vector3(1, 0, 0) : new Vector3(-1, 0, 0);
                Vector3 wallCenter = new(gx * cs, wh * 0.5f, (gy + 0.5f) * cs);
                AddFramedMirror(result, wallCenter + normal * faceOffset, normal, cs, wh);
            }
        }

        return result;
    }

    // Mounts a frame quad (flush on the wall) and a slightly-larger-inset mirror quad
    // a hair in front of it, both facing +normal (into the cell). cross(uAxis, +Y) == normal.
    private static void AddFramedMirror(List<Tracable> list, Vector3 wallCenter, Vector3 normal, float cs, float wh)
    {
        Vector3 n = Vector3.Normalize(normal);
        Vector3 up = new(0, 1, 0);
        Vector3 uAxis = Vector3.Normalize(Vector3.Cross(up, n)); // along the wall; cross(uAxis, up) == n

        const float frameWFrac = 0.56f, frameHFrac = 0.62f;
        const float mirrorWFrac = 0.42f, mirrorHFrac = 0.48f;

        // Frame just off the wall; mirror a little further into the cell (so the border reads).
        list.Add(Panel(wallCenter + n * 0.02f, uAxis, up, frameWFrac * cs, frameHFrac * wh, FrameMat));
        list.Add(Panel(wallCenter + n * 0.045f, uAxis, up, mirrorWFrac * cs, mirrorHFrac * wh, MirrorMat));
    }

    // A w×h quad centred at <paramref name="center"/> spanning uAxis (width) and
    // vAxis (height); its geometric normal is cross(uAxis, vAxis).
    private static TracableRectangle Panel(Vector3 center, Vector3 uAxis, Vector3 vAxis, float w, float h, MaterialData mat)
    {
        Vector3 l1 = center - uAxis * (w * 0.5f) - vAxis * (h * 0.5f);
        Vector3 l2 = l1 + uAxis * w;
        Vector3 l3 = l1 + vAxis * h;
        return new TracableRectangle((l1, l2, l3), mat);
    }

    private static SpectralData FlatSpectrum(float value)
    {
        const int min = 360, max = 830, step = 5;
        int count = (max - min) / step + 1;
        var wavelengths = new int[count];
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            wavelengths[i] = min + i * step;
            values[i] = value;
        }
        return new SpectralData(wavelengths, values, (float[])values.Clone());
    }
}
