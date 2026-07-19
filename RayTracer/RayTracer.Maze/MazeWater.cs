using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Places ornamental water pools in a seeded subset of <b>Outdoor</b> cells (outdoor-area-plan.md §O6,
/// spectral §1.3). Each pool is a small raised basin: a horizontal <see cref="SurfaceKind.Dielectric"/>
/// water surface (ior 1.33) over a dark diffuse basin, walled by a low stone rim. The surface Fresnel-
/// reflects the sky (§O2/§O3 — relying on the sky-in-reflections fix) and refracts down to the basin,
/// with Beer–Lambert absorption (§2.3, <c>absorptionRgb</c>) tinting the transmitted light blue-green over
/// the water depth. Deterministic per-cell placement (skips the start/goal), so a run is reproducible.
///
/// Flat surface for now (the "Medium" tier): it mirrors and tints. Rippled normals + focused caustics on
/// the basin are the deferred "High" tier.
/// </summary>
internal static class MazeWater
{
    /// <param name="Chance">Probability a given Outdoor cell holds a pool.</param>
    /// <param name="Seed">Seed for reproducible placement.</param>
    internal sealed record Options(float Chance = 0.16f, int Seed = 0);

    private const float Inset = 0.22f;    // grass border: pool spans the inner (1 − 2·Inset) of the cell
    private const float BasinY = 0.02f;   // basin floor, just above the grass
    private const float WaterY = 0.22f;   // water surface height (≈0.2 deep)

    // Water: a very weakly-absorbing dielectric — these are shallow ornamental pools (~0.2 deep), not the
    // open sea, so the water is nearly clear: you read the bottom plus a sky reflection at grazing angles,
    // with only a faint blue-green depth tint (σ red > green > blue, §2.3). Strong σ here would make a
    // shallow pool look azure, which it shouldn't.
    private static readonly MaterialData WaterMat = new(
        "pool-water", "Water", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        spectralData: null, surface: SurfaceKind.Dielectric, transmission: 0.95f, cauchyA: 1.33f, cauchyB: 0f,
        absorptionRgb: new Vector3(1.6f, 0.7f, 0.2f));

    // Light sandy-stone bottom so the shallow pool reads as "clear water over a visible bottom" rather than
    // a dark blue void; the rim is a wet mid-grey stone.
    private static readonly MaterialData BasinMat = ColoredDiffuse("pool-basin", new Vector3(0.52f, 0.48f, 0.40f));
    private static readonly MaterialData SideMat = ColoredDiffuse("pool-side", new Vector3(0.28f, 0.28f, 0.30f));

    /// <summary>Deterministic per-cell selection: the Outdoor cells (row-major) that hold a pool.</summary>
    internal static IEnumerable<(int gx, int gy)> Cells(Maze maze, Options opt)
    {
        for (int gy = 0; gy < maze.Height; gy++)
            for (int gx = 0; gx < maze.Width; gx++)
            {
                if (!maze.IsOutdoor(gx, gy)) continue;
                if ((gx == 0 && gy == 0) || (gx == maze.Width - 1 && gy == maze.Height - 1)) continue; // start/goal
                uint h = (uint)(gx * 2654435761 ^ gy * 40503431 ^ opt.Seed * 374761393);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                if ((h & 0xFFFFFFu) / 16777216f < opt.Chance)
                    yield return (gx, gy);
            }
    }

    internal static List<Tracable> Build(Maze maze, Options opt, float cellSize = MazeGeometryBuilder.CellSize)
    {
        var result = new List<Tracable>();
        float cs = cellSize, m = Inset * cs;

        foreach ((int gx, int gy) in Cells(maze, opt))
        {
            float x0 = gx * cs + m, x1 = (gx + 1) * cs - m;
            float z0 = gy * cs + m, z1 = (gy + 1) * cs - m;

            // Dark basin bottom.
            result.Add(new TracableRectangle(
                (new Vector3(x0, BasinY, z0), new Vector3(x1, BasinY, z0), new Vector3(x0, BasinY, z1)), BasinMat));

            // Four rim/side walls holding the water (double-sided: seen from inside the pool and from the
            // grass border outside), basin floor up to the water line.
            result.Add(new TracableRectangle(
                (new Vector3(x0, BasinY, z0), new Vector3(x1, BasinY, z0), new Vector3(x0, WaterY, z0)), SideMat));
            result.Add(new TracableRectangle(
                (new Vector3(x0, BasinY, z1), new Vector3(x1, BasinY, z1), new Vector3(x0, WaterY, z1)), SideMat));
            result.Add(new TracableRectangle(
                (new Vector3(x0, BasinY, z0), new Vector3(x0, BasinY, z1), new Vector3(x0, WaterY, z0)), SideMat));
            result.Add(new TracableRectangle(
                (new Vector3(x1, BasinY, z0), new Vector3(x1, BasinY, z1), new Vector3(x1, WaterY, z0)), SideMat));

            // Water surface (dielectric).
            result.Add(new TracableRectangle(
                (new Vector3(x0, WaterY, z0), new Vector3(x1, WaterY, z0), new Vector3(x0, WaterY, z1)), WaterMat));
        }

        return result;
    }

    /// <summary>Diffuse spectral material from an RGB tint via broad overlapping Gaussian bands (matches the
    /// Program/Phase3Scene <c>ColoredDiffuse</c>/<c>Foliage</c> helper).</summary>
    private static MaterialData ColoredDiffuse(string id, Vector3 rgb)
    {
        const int min = 360, max = 830, step = 5;
        int count = (max - min) / step + 1;
        var wavelengths = new int[count];
        var values = new float[count];
        for (int i = 0; i < count; i++)
        {
            int w = min + i * step;
            wavelengths[i] = w;
            float r = MathF.Exp(-MathF.Pow((w - 620f) / 70f, 2f));
            float g = MathF.Exp(-MathF.Pow((w - 540f) / 60f, 2f));
            float b = MathF.Exp(-MathF.Pow((w - 460f) / 55f, 2f));
            values[i] = Math.Clamp(rgb.X * r + rgb.Y * g + rgb.Z * b, 0f, 1f);
        }
        return new MaterialData(id, id, null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            new SpectralData(wavelengths, values, (float[])values.Clone()), SurfaceKind.Diffuse);
    }
}
