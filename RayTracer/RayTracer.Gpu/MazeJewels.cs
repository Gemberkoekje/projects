using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Places floating faceted crystals — the maze's dispersive "signature object"
/// (spectral-effects-plan.md §2.1). Each jewel is a glass cube balanced on its
/// corner (its space diagonal vertical, giving the classic hexagonal crystal
/// silhouette) built from six <see cref="SurfaceKind.Dielectric"/> quad faces with
/// an exaggerated Cauchy-B dispersion coefficient, so light bending through it splits
/// into colour ("fire"). It hangs at mid-height in the centre of a seeded subset of
/// cells. The renderer geometry is quad-only (every face is a parallelogram), which
/// rules out triangular gem facets — a cube on its corner is the cleanest closed,
/// all-parallelogram solid that still reads as a jewel.
///
/// Placement is a deterministic per-cell hash (so a run is reproducible and the demo
/// can find the nearest jewel without rebuilding), skipping the start and goal cells
/// so the camera never spawns inside one and the goal logo stays clear.
/// </summary>
internal static class MazeJewels
{
    /// <param name="Chance">Probability a given cell floats a jewel.</param>
    /// <param name="Seed">Seed for reproducible placement.</param>
    /// <param name="CauchyB">Dispersion strength (µm²); 0 = clear, higher = more "fire".</param>
    internal sealed record Options(float Chance = 0.08f, int Seed = 0, float CauchyB = 0.05f);

    // Unit-cube faces as parallelograms (l1, l1+e1, l1+e2) on [-1,1]³. Winding is
    // irrelevant for a dielectric (Optics.Refract orients the normal against the ray).
    private static readonly (Vector3 l1, Vector3 l2, Vector3 l3)[] CubeFaces =
    [
        (new(1, -1, -1), new(1, 1, -1), new(1, -1, 1)),    // +X
        (new(-1, -1, -1), new(-1, -1, 1), new(-1, 1, -1)), // -X
        (new(-1, 1, -1), new(1, 1, -1), new(-1, 1, 1)),    // +Y
        (new(-1, -1, -1), new(-1, -1, 1), new(1, -1, -1)), // -Y
        (new(-1, -1, 1), new(1, -1, 1), new(-1, 1, 1)),    // +Z
        (new(-1, -1, -1), new(1, -1, -1), new(-1, 1, -1)), // -Z
    ];

    // Sized and hung so it floats clear of the walker's eye line: a cube on its corner
    // reaches ±(ScaleFrac·CellSize·√3) vertically, so at CellSize 2 / WallHeight 2 the jewel
    // spans y≈1.15→1.85 — above eye height (wallHeight·0.5 = 1.0) yet below the ceiling — and
    // the camera passes underneath instead of stopping inside it.
    private const float ScaleFrac = 0.10f;   // cube half-extent as a fraction of the cell size
    private const float HeightFrac = 0.75f;   // centre height as a fraction of the wall height

    private static MaterialData JewelMat(float cauchyB) => new(
        "jewel", "Jewel", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        spectralData: null, surface: SurfaceKind.Dielectric, transmission: 0.98f, cauchyA: 1.5f, cauchyB: cauchyB);

    /// <summary>Deterministic per-cell selection: the world-space centre (and yaw) of every
    /// cell that floats a jewel, in row-major order. Shared by <see cref="Build"/> and the
    /// demo camera so both agree without a rebuild.</summary>
    internal static IEnumerable<(Vector3 center, float yaw)> Placements(
        Maze maze, Options opt,
        float cellSize = MazeGeometryBuilder.CellSize,
        float wallHeight = MazeGeometryBuilder.WallHeight)
    {
        for (int gy = 0; gy < maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                // Skip the start (spawn) and goal (logo) cells so a jewel never blocks them.
                if ((gx == 0 && gy == 0) || (gx == maze.Width - 1 && gy == maze.Height - 1)) continue;

                uint h = (uint)(gx * 73856093 ^ gy * 19349663 ^ opt.Seed * 2654435761);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                if ((h & 0xFFFFFFu) / 16777216f >= opt.Chance) continue;

                var center = new Vector3((gx + 0.5f) * cellSize, HeightFrac * wallHeight, (gy + 0.5f) * cellSize);
                float yaw = ((h >> 8) & 0xFFFFu) / 65536f * MathF.Tau;
                yield return (center, yaw);
            }
        }
    }

    internal static List<Tracable> Build(
        Maze maze, Options opt,
        float cellSize = MazeGeometryBuilder.CellSize,
        float wallHeight = MazeGeometryBuilder.WallHeight)
    {
        var result = new List<Tracable>();
        MaterialData mat = JewelMat(opt.CauchyB);
        float scale = ScaleFrac * cellSize;

        // Rotation that stands the cube on a corner: map the space diagonal (1,1,1) to +Y.
        Vector3 diag = Vector3.Normalize(new Vector3(1, 1, 1));
        Vector3 up = new(0, 1, 0);
        Vector3 axis = Vector3.Normalize(Vector3.Cross(diag, up));
        float onCorner = MathF.Acos(Math.Clamp(Vector3.Dot(diag, up), -1f, 1f));
        Quaternion stand = Quaternion.CreateFromAxisAngle(axis, onCorner);

        foreach (var (center, yaw) in Placements(maze, opt, cellSize, wallHeight))
        {
            Quaternion rot = Quaternion.CreateFromAxisAngle(up, yaw) * stand;
            foreach (var (l1, l2, l3) in CubeFaces)
                result.Add(new TracableRectangle(
                    (Place(l1, rot, scale, center), Place(l2, rot, scale, center), Place(l3, rot, scale, center)), mat));
        }

        return result;
    }

    private static Vector3 Place(Vector3 localCorner, Quaternion rot, float scale, Vector3 center)
        => center + Vector3.Transform(localCorner * scale, rot);
}
