using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Places iridescent oil puddles on the floor of a seeded subset of cells
/// (spectral-effects-plan.md §2.2 — thin-film interference). Each puddle is a single flat
/// <see cref="SurfaceKind.ThinFilm"/> quad with a fairly thick film, so the reflected colour
/// bands smoothly through the rainbow across its depth as the view angle sweeps (grazing at
/// the near edge, steeper at the far edge) and shifts again as the viewer moves
/// (goniochromism) — the continuous sheen of real oil rather than hard tiles. Each puddle
/// draws its thickness from a shared palette (distinct ids so the GPU scene packer keeps
/// them separate but a puddle count never multiplies the material count).
///
/// Placement is a deterministic per-cell hash (reproducible; skips the start and goal cells
/// so a puddle never clutters the spawn or the goal logo), matching <see cref="MazeJewels"/>.
/// </summary>
internal static class MazeOilSlicks
{
    /// <param name="Chance">Probability a given cell floats an oil puddle.</param>
    /// <param name="Seed">Seed for reproducible placement.</param>
    internal sealed record Options(float Chance = 0.06f, int Seed = 0);

    private const int Tiles = 14;        // Tiles × Tiles floor quads — only for the puddle's rounded outline
    private const int Levels = 6;         // shared palette (per-puddle base thickness, for variety)
    private const float MinNm = 480f, MaxNm = 760f;
    private const float SpatialAmpNm = 90f;  // small: the colour is mostly view-angle driven, so it shifts as you move
    private const float FilmContrast = 0.5f; // pastel, not a neon rainbow
    private const float FilmIor = 1.45f; // oil on water
    private const float Albedo = 0.35f;  // dark, so it reads as a wet sheen, not a glowing plate
    private const float PuddleFrac = 0.9f; // fraction of the cell the puddle spans
    private const float Eps = 0.01f;     // lift off the floor to avoid z-fighting

    // One material per base thickness, built once (distinct ids so the packer keeps them).
    // Every tile of a puddle shares one material; the colour variation is entirely the
    // per-pixel Optics.FilmSwirl (via FilmSpatialAmpNm), so there are no per-tile steps —
    // the tiles exist only to cut a rounded, irregular outline.
    private static readonly MaterialData[] Palette = BuildPalette();

    private static MaterialData[] BuildPalette()
    {
        var p = new MaterialData[Levels];
        for (int i = 0; i < Levels; i++)
        {
            float thickness = MinNm + (MaxNm - MinNm) * i / (Levels - 1);
            p[i] = new MaterialData(
                $"oil-{i}", "Oil", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                FlatSpectrum(Albedo), SurfaceKind.ThinFilm, cauchyA: FilmIor,
                filmThicknessNm: thickness, filmSpatialAmpNm: SpatialAmpNm, filmContrast: FilmContrast);
        }
        return p;
    }

    /// <summary>Deterministic per-cell selection: the cells (row-major) that get a puddle,
    /// each with a base-thickness palette index and a phase for the rim wobble.</summary>
    internal static IEnumerable<(int gx, int gy, int level, float phase)> Cells(Maze maze, Options opt)
    {
        for (int gy = 0; gy < maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                if ((gx == 0 && gy == 0) || (gx == maze.Width - 1 && gy == maze.Height - 1)) continue;

                uint h = (uint)(gx * 40503431 ^ gy * 15485863 ^ opt.Seed * 2654435761);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                if ((h & 0xFFFFFFu) / 16777216f >= opt.Chance) continue;

                int level = (int)((h >> 8) % Levels);
                float phase = ((h >> 12) & 0xFFFFu) / 65536f * MathF.Tau;
                yield return (gx, gy, level, phase);
            }
        }
    }

    internal static List<Tracable> Build(
        Maze maze, Options opt, float cellSize = MazeGeometryBuilder.CellSize)
    {
        var result = new List<Tracable>();
        float cs = cellSize;
        float margin = (1f - PuddleFrac) * 0.5f;

        foreach (var (gx, gy, level, phase) in Cells(maze, opt))
        {
            MaterialData mat = Palette[level];
            float px0 = (gx + margin) * cs, px1 = (gx + 1 - margin) * cs;
            float pz0 = (gy + margin) * cs, pz1 = (gy + 1 - margin) * cs;

            for (int ty = 0; ty < Tiles; ty++)
            {
                for (int tx = 0; tx < Tiles; tx++)
                {
                    float du = (tx + 0.5f) / Tiles - 0.5f, dv = (ty + 0.5f) / Tiles - 0.5f;

                    // Rounded, slightly irregular rim so the puddle isn't a hard rectangle.
                    float rim = 0.45f + 0.06f * MathF.Sin(9f * MathF.Atan2(dv, du) + phase);
                    if (du * du + dv * dv > rim * rim) continue;

                    float x0 = px0 + (px1 - px0) * tx / Tiles, x1 = px0 + (px1 - px0) * (tx + 1) / Tiles;
                    float z0 = pz0 + (pz1 - pz0) * ty / Tiles, z1 = pz0 + (pz1 - pz0) * (ty + 1) / Tiles;

                    // Flat on the floor, facing up (edge1 = +X, edge2 = +Z), like the floor logo.
                    result.Add(new TracableRectangle(
                        (new Vector3(x0, Eps, z0), new Vector3(x1, Eps, z0), new Vector3(x0, Eps, z1)),
                        mat));
                }
            }
        }

        return result;
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
