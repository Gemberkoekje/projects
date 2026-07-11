using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Replaces a seeded subset of the maze's interior walls with clear-glass windows
/// (spectral-effects-plan.md §1.2). The wall that would separate two cells is omitted
/// (via <see cref="MazeGeometryBuilder"/>'s <c>skipWall</c> predicate) and a window is
/// built in its place, so a viewer sees the adjacent room refracted through the pane.
/// Each window reads as a real window set into the wall: a brick knee-wall (spandrel)
/// below, a stone sill ledge, a thin dielectric glass slab (two
/// <see cref="SurfaceKind.Dielectric"/> quads a small gap apart), and a short brick
/// header above. When the maze uses thick walls the window is built full-width and
/// thick to match (two brick faces + a lintel + side jambs where no wall continues), so
/// it joins its neighbours seamlessly. Selection is a deterministic per-wall hash so the
/// skip predicate and the geometry agree and a run is reproducible.
/// </summary>
internal static class MazeWindows
{
    /// <param name="Chance">Probability a given interior wall becomes a window.</param>
    /// <param name="Seed">Seed for reproducible placement.</param>
    /// <param name="StainedChance">Probability a window's glass is a coloured stained pane
    /// (Beer–Lambert absorption, §2.3) rather than clear. 0 = all clear.</param>
    internal sealed record Options(float Chance = 0.15f, int Seed = 0, float StainedChance = 0f);

    private static readonly MaterialData GlassMat = new(
        "glass-window", "Glass", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        spectralData: null, surface: SurfaceKind.Dielectric, transmission: 0.95f, cauchyA: 1.5f, cauchyB: 0f);

    private static MaterialData Stained(string id, float r, float g, float b) => new(
        id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        spectralData: null, surface: SurfaceKind.Dielectric, transmission: 0.95f, cauchyA: 1.5f, cauchyB: 0f,
        absorptionRgb: new Vector3(r, g, b));

    // Stained-glass tints — σ at the R/G/B anchors, high because a window pane is thin so the
    // in-glass path is short (§2.3). A pane transmits its low-σ channel(s).
    private static readonly MaterialData[] StainedPalette =
    [
        Stained("stain-red", 1.5f, 22f, 28f),
        Stained("stain-amber", 1.5f, 6f, 30f),
        Stained("stain-green", 22f, 2.5f, 24f),
        Stained("stain-teal", 26f, 3f, 7f),
        Stained("stain-blue", 28f, 20f, 1.5f),
        Stained("stain-violet", 6f, 26f, 4f),
    ];

    /// <summary>The glass for a given window — clear, or a deterministically-chosen stained
    /// tint when <see cref="Options.StainedChance"/> fires for that pane.</summary>
    private static MaterialData WindowGlass(int gx, int gy, bool horizontal, Options opt)
    {
        if (opt.StainedChance <= 0f) return GlassMat;
        uint h = (uint)(gx * 2654435761 ^ gy * 40503431 ^ (horizontal ? 1013904223 : 0) ^ opt.Seed * 374761393);
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
        if ((h & 0xFFFFFFu) / 16777216f >= opt.StainedChance) return GlassMat;
        return StainedPalette[(int)((h >> 8) % (uint)StainedPalette.Length)];
    }

    private const float SlabHalfGap = 0.03f;
    private const float SillFrac = 0.32f;
    private const float HeaderFrac = 0.86f;
    private const float SillOverhang = 0.05f;

    /// <summary>
    /// The <c>skipWall</c> predicate for <see cref="MazeGeometryBuilder.Build"/>: true for
    /// exactly the interior walls this run turns into windows, so those brick walls are
    /// omitted and the window replaces them.
    /// </summary>
    internal static Func<int, int, bool, bool> SkipPredicate(Maze maze, Options opt, ISet<(int, int, bool)> exclude)
        => (gx, gy, horizontal) => IsWindowWall(maze, opt, gx, gy, horizontal) && !exclude.Contains((gx, gy, horizontal));

    private static bool IsWindowWall(Maze maze, Options opt, int gx, int gy, bool horizontal)
    {
        if (horizontal)
        {
            if (gy <= 0 || gy >= maze.Height) return false;
        }
        else
        {
            if (gx <= 0 || gx >= maze.Width) return false;
        }

        uint h = (uint)(gx * 73856093 ^ gy * 19349663 ^ (horizontal ? 83492791 : 0) ^ opt.Seed * 2654435761);
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
        return (h & 0xFFFFFFu) / 16777216f < opt.Chance;
    }

    // Collinear-neighbour checks (same convention as MazeGeometryBuilder), so a window's
    // side jamb is added only where no wall/window continues the run.
    private static bool HasVWall(Maze m, int gx, int gy)
        => gx >= 0 && gx <= m.Width && gy >= 0 && gy < m.Height
        && (gx < m.Width ? m.HasWall(gx, gy, Wall.West) : m.HasWall(m.Width - 1, gy, Wall.East));

    private static bool HasHWall(Maze m, int gx, int gy)
        => gx >= 0 && gx < m.Width && gy >= 0 && gy <= m.Height
        && (gy < m.Height ? m.HasWall(gx, gy, Wall.North) : m.HasWall(gx, m.Height - 1, Wall.South));

    internal static List<Tracable> Build(
        Maze maze, Options opt, ISet<(int, int, bool)> exclude,
        MaterialData wallMaterial, MaterialData mortarMaterial, MaterialData sillMaterial,
        float cellSize = MazeGeometryBuilder.CellSize,
        float wallHeight = MazeGeometryBuilder.WallHeight,
        float wallThickness = 0f)
    {
        var result = new List<Tracable>();
        float cs = cellSize, wh = wallHeight;

        // Vertical interior walls (grid line x = gx·cs, separating (gx-1,gy)↔(gx,gy)).
        for (int gx = 1; gx < maze.Width; gx++)
        {
            for (int gy = 0; gy < maze.Height; gy++)
            {
                if (!maze.HasWall(gx, gy, Wall.West) || !IsWindowWall(maze, opt, gx, gy, horizontal: false)
                    || exclude.Contains((gx, gy, false))) continue;

                AddWindow(result, plane: gx * cs, perpAxis: new Vector3(1, 0, 0), widthAxis: new Vector3(0, 0, 1),
                    lo: gy * cs, hi: (gy + 1) * cs, wh, wallThickness,
                    nearHasWall: HasVWall(maze, gx, gy - 1), farHasWall: HasVWall(maze, gx, gy + 1),
                    wallMaterial, mortarMaterial, sillMaterial, WindowGlass(gx, gy, false, opt));
            }
        }

        // Horizontal interior walls (grid line z = gy·cs, separating (gx,gy-1)↔(gx,gy)).
        for (int gy = 1; gy < maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                if (!maze.HasWall(gx, gy, Wall.North) || !IsWindowWall(maze, opt, gx, gy, horizontal: true)
                    || exclude.Contains((gx, gy, true))) continue;

                AddWindow(result, plane: gy * cs, perpAxis: new Vector3(0, 0, 1), widthAxis: new Vector3(1, 0, 0),
                    lo: gx * cs, hi: (gx + 1) * cs, wh, wallThickness,
                    nearHasWall: HasHWall(maze, gx - 1, gy), farHasWall: HasHWall(maze, gx + 1, gy),
                    wallMaterial, mortarMaterial, sillMaterial, WindowGlass(gx, gy, true, opt));
            }
        }

        return result;
    }

    private static void AddWindow(
        List<Tracable> list, float plane, Vector3 perpAxis, Vector3 widthAxis, float lo, float hi, float wh, float t,
        bool nearHasWall, bool farHasWall,
        MaterialData wall, MaterialData mortar, MaterialData sill, MaterialData glass)
    {
        Vector3 up = new(0, 1, 0);
        Vector3 p = perpAxis * plane;                 // the wall-plane offset (only the perp component is nonzero)
        Vector3 loP = widthAxis * lo, hiP = widthAxis * hi;
        float width = hi - lo;
        float sillY = SillFrac * wh, topY = HeaderFrac * wh;
        float ht = t * 0.5f;

        // Brick knee-wall + header faces (two, a thickness apart, or one coincident pair when thin).
        foreach (float off in ht > 0f ? new[] { -ht, ht } : new[] { 0f })
        {
            Vector3 basePt = p + perpAxis * off;
            AddBrick(list, basePt + loP, widthAxis * width, up * sillY, wall, mortar, wh);          // knee-wall
            AddBrick(list, basePt + loP + up * topY, widthAxis * width, up * (wh - topY), wall, mortar, wh); // header
        }

        // Glass slab (two panes), full width, between sill and header.
        AddGlass(list, p + perpAxis * SlabHalfGap + loP + up * sillY, widthAxis * width, up * (topY - sillY), glass);
        AddGlass(list, p - perpAxis * SlabHalfGap + loP + up * sillY, widthAxis * width, up * (topY - sillY), glass);

        // Stone sill ledge on the knee-wall, overhanging into both cells.
        float sillReach = ht + SillOverhang;
        list.Add(new TracableRectangle(
            (p - perpAxis * sillReach + loP + up * sillY, p - perpAxis * sillReach + hiP + up * sillY,
             p + perpAxis * sillReach + loP + up * sillY), sill));

        if (ht > 0f)
        {
            // Lintel (header underside) so the top of the opening reads as thick brick.
            list.Add(new BrickRectangle(
                (p - perpAxis * ht + loP + up * topY, p - perpAxis * ht + hiP + up * topY,
                 p + perpAxis * ht + loP + up * topY), wall, mortar));

            // Side jambs (reveals) where no wall continues the run — the "180° corner".
            if (!nearHasWall) AddJamb(list, p + loP, perpAxis, up, ht, wh, wall);
            if (!farHasWall) AddJamb(list, p + hiP, perpAxis, up, ht, wh, wall);
        }
    }

    private static void AddBrick(List<Tracable> list, Vector3 l1, Vector3 edgeU, Vector3 edgeV, MaterialData wall, MaterialData mortar, float wallHeight)
    {
        int bricksDown = Math.Max(1, (int)MathF.Round(5f * edgeV.Length() / wallHeight));
        list.Add(new BrickRectangle((l1, l1 + edgeU, l1 + edgeV), wall, mortar, bricksAcross: 2f, bricksDown: bricksDown));
    }

    private static void AddGlass(List<Tracable> list, Vector3 l1, Vector3 edgeU, Vector3 edgeV, MaterialData glass)
        => list.Add(new TracableRectangle((l1, l1 + edgeU, l1 + edgeV), glass));

    // A full-height brick reveal closing one end of the opening (spans the wall thickness).
    private static void AddJamb(List<Tracable> list, Vector3 endCentreFloor, Vector3 perpAxis, Vector3 up, float ht, float wh, MaterialData wall)
    {
        Vector3 l1 = endCentreFloor - perpAxis * ht;
        list.Add(new TracableRectangle((l1, l1 + perpAxis * (2f * ht), l1 + up * wh), wall));
    }
}
