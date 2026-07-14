using System.Numerics;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Bubble machines (spectral-effects-plan §2.6): a few emitters scattered through the maze, each
/// continuously firing a rising stream of small iridescent soap bubbles — thin-film-on-a-sphere
/// (§0.4 sphere + <see cref="SurfaceKind.Bubble"/>). Bubbles are the only spheres (and the only
/// moving geometry) in the maze. Each bubble rises from its emitter in a gentle cone, wobbles,
/// grows then pops near the top and respawns; the bubbles of one emitter are evenly staggered in
/// phase, so the emitter reads as a continuous stream (a bubble blaster).
///
/// Emitter placement (which cells) is a deterministic per-cell hash (reproducible; skips start/goal),
/// and each emitter's per-bubble stream seeds are derived from that hash — so <see cref="Bubbles"/>,
/// <see cref="Build"/>, and <see cref="Animate"/> all agree on count + order without a rebuild.
/// </summary>
internal static class MazeBubbles
{
    /// <param name="Chance">Probability a given cell is a bubble emitter.</param>
    /// <param name="Seed">Seed for reproducible placement.</param>
    /// <param name="ThicknessNm">Soap-film thickness (nm) — sets the interference band spacing.</param>
    /// <param name="Radius">Base bubble radius (world units) — small, so a stream reads as many bubbles.</param>
    /// <param name="PerEmitter">Bubbles in flight per emitter (the stream length).</param>
    /// <param name="Lifetime">Seconds for a bubble to rise from the emitter and pop. Long (slow rise):
    /// bubbles no longer get a per-pixel accumulation reset, so a fast rise would smear them vertically
    /// in the temporal accumulator — a slow climb keeps each bubble reading as a round bubble.</param>
    /// <param name="RiseHeight">How high the stream climbs (world units).</param>
    /// <param name="Spread">How far the cone fans out horizontally by the top (world units).</param>
    internal sealed record Options(
        float Chance = 0.035f, int Seed = 0, float ThicknessNm = 440f, float Radius = 0.11f,
        int PerEmitter = 10, float Lifetime = 24.0f, float RiseHeight = 1.6f, float Spread = 0.45f);

    /// <summary>One bubble in flight: its emitter origin plus the deterministic seeds that shape its
    /// arc through the stream. <see cref="Animate"/> turns these + a time into a centre and radius.</summary>
    /// <param name="Origin">Emitter mouth (cell centre, near the floor).</param>
    /// <param name="PhaseOffset">Stagger within the stream in [0,1); evenly spaced → continuous emission.</param>
    /// <param name="Drift">Horizontal cone direction it leans into as it rises.</param>
    /// <param name="WobblePhase">Phase of the side-to-side wobble.</param>
    /// <param name="RadiusScale">Per-bubble size variation (small bubbles are not all identical).</param>
    internal readonly record struct Bubble(
        Vector3 Origin, float PhaseOffset, Vector3 Drift, float WobblePhase, float RadiusScale);

    private const float EmitFloor = 0.25f; // emitter mouth height (near the floor, like a bubble machine)

    /// <summary>The soap-bubble material shared by every bubble: a flat white base reflectance (the
    /// visible colour is all thin-film interference), film index ~1.33, thickness in nm.</summary>
    internal static MaterialData BubbleMaterial(Options opt) => new(
        "maze-bubble", "MazeBubble", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
        FlatSpectrum(1.0f), SurfaceKind.Bubble, cauchyA: 1.33f, cauchyB: 0f, filmThicknessNm: opt.ThicknessNm);

    /// <summary>Deterministic emitter mouths (cell centres near the floor) for the cells that fire a
    /// bubble stream, in row-major order. Exposed so a demo can aim at the nearest stream.
    /// <paramref name="occupied"/> cells (already holding another feature) are skipped so a stream
    /// never shares a square with a jewel, oil slick, mirror, or sign.</summary>
    internal static IReadOnlyList<Vector3> Emitters(
        Maze maze, Options opt, IReadOnlySet<(int, int)>? occupied = null,
        float cellSize = MazeGeometryBuilder.CellSize)
    {
        var result = new List<Vector3>();
        foreach ((Vector3 origin, uint _) in EmitterCells(maze, opt, occupied, cellSize))
            result.Add(origin);
        return result;
    }

    /// <summary>The flattened bubble list — <c>PerEmitter</c> staggered bubbles per emitter, in the
    /// same order as <see cref="PackedScene.Spheres"/> (bubbles are the only spheres). Each emitter's
    /// bubbles fan into a cone with per-bubble drift / wobble / size derived from the cell hash.</summary>
    internal static IReadOnlyList<Bubble> Bubbles(
        Maze maze, Options opt, IReadOnlySet<(int, int)>? occupied = null,
        float cellSize = MazeGeometryBuilder.CellSize)
    {
        var result = new List<Bubble>();
        foreach ((Vector3 origin, uint h) in EmitterCells(maze, opt, occupied, cellSize))
        {
            for (int b = 0; b < opt.PerEmitter; b++)
            {
                uint bh = h ^ (uint)(b * 2246822519u);
                bh ^= bh >> 15; bh *= 2246822519u; bh ^= bh >> 13; bh *= 3266489917u; bh ^= bh >> 16;
                float ang = (bh & 0xFFFFu) / 65536f * MathF.Tau;          // cone azimuth
                float lean = 0.3f + ((bh >> 16) & 0xFFu) / 255f * 0.7f;   // how far it leans out
                var drift = new Vector3(MathF.Cos(ang) * lean, 0f, MathF.Sin(ang) * lean);
                float wobblePhase = ((bh >> 8) & 0xFFu) / 255f * MathF.Tau;
                float radiusScale = 0.6f + ((bh >> 24) & 0xFFu) / 255f * 0.7f; // 0.6..1.3
                float phaseOffset = (float)b / opt.PerEmitter;            // even stagger → continuous stream
                result.Add(new Bubble(origin, phaseOffset, drift, wobblePhase, radiusScale));
            }
        }
        return result;
    }

    /// <summary>The static bubble spheres for packing — one <see cref="Sphere"/> per flight bubble at
    /// its time-0 position, in <see cref="Bubbles"/> order. <see cref="Animate"/> then moves them each
    /// frame via the renderer's sphere refit.</summary>
    internal static List<Tracable> Build(
        Maze maze, Options opt, IReadOnlySet<(int, int)>? occupied = null,
        float cellSize = MazeGeometryBuilder.CellSize)
    {
        MaterialData mat = BubbleMaterial(opt);
        IReadOnlyList<Bubble> bubbles = Bubbles(maze, opt, occupied, cellSize);
        var centers = new Vector3[bubbles.Count];
        var radii = new float[bubbles.Count];
        Animate(bubbles, opt, 0f, centers, radii);

        var result = new List<Tracable>(bubbles.Count);
        for (int i = 0; i < bubbles.Count; i++)
            result.Add(new Sphere(centers[i], MathF.Max(radii[i], 1e-3f), mat));
        return result;
    }

    /// <summary>Evaluates every bubble's current centre and radius at wall-clock <paramref name="time"/>
    /// (seconds), writing them in <see cref="Bubbles"/> order. Bubbles rise from their emitter in a
    /// cone, wobble side to side, grow from nothing, hold, then pop back to ~0 (invisible) and respawn
    /// — so the sphere count stays fixed (no BLAS resize, just a per-frame refit) and the staggered
    /// phases keep each emitter a continuous stream.</summary>
    internal static void Animate(
        IReadOnlyList<Bubble> bubbles, Options opt, float time, Span<Vector3> centers, Span<float> radii)
    {
        for (int i = 0; i < bubbles.Count; i++)
        {
            Bubble b = bubbles[i];
            float p = Frac(time / opt.Lifetime + b.PhaseOffset);   // 0..1 age along the stream
            float rise = opt.RiseHeight * p;
            float fan = opt.Spread * p;                            // cone widens as it climbs
            // A slow, gentle wobble. Bubbles no longer get a per-pixel accumulation reset, so this
            // sideways drift smears them within the temporal accumulator — keep the amplitude small
            // and the frequency low so the smear stays sub-radius and the bubble still reads as round.
            float wob = 0.015f * MathF.Sin(time * 0.35f + b.WobblePhase);
            var perp = new Vector3(-b.Drift.Z, 0f, b.Drift.X);     // sideways wobble axis
            centers[i] = b.Origin + new Vector3(0f, rise, 0f) + b.Drift * fan + perp * wob;
            radii[i] = opt.Radius * b.RadiusScale * RadiusEnvelope(p);
        }
    }

    // Radius over the lifecycle: swell from 0 to full by 10%, hold, then pop (fast shrink to 0) over
    // the last 15%. Returns a factor in [0,1] of the bubble's base radius.
    private static float RadiusEnvelope(float p)
    {
        const float grow = 0.10f, pop = 0.85f;
        if (p < grow) return Smoothstep(p / grow);
        if (p < pop) return 1f;
        return Smoothstep(1f - (p - pop) / (1f - pop));
    }

    private static IEnumerable<(Vector3 origin, uint hash)> EmitterCells(
        Maze maze, Options opt, IReadOnlySet<(int, int)>? occupied, float cellSize)
    {
        for (int gy = 0; gy < maze.Height; gy++)
        {
            for (int gx = 0; gx < maze.Width; gx++)
            {
                // Skip the start (spawn) and goal (logo) cells so a stream never blocks them, and any
                // cell already holding another feature (a jewel, oil slick, mirror, or sign).
                if ((gx == 0 && gy == 0) || (gx == maze.Width - 1 && gy == maze.Height - 1)) continue;
                if (occupied is not null && occupied.Contains((gx, gy))) continue;

                // A bubble salt (0x00B0BB1E) decorrelates this from the jewel/oil-slick per-cell hash,
                // which share the same formula + seed — without it the low-hash cells would be selected
                // by all three at once, so bubbles would pile onto the very cells the exclusion removes
                // (leaving almost none). Salted, bubbles pick their own cells; exclusion just trims the
                // rare incidental overlap.
                uint h = (uint)(gx * 73856093 ^ gy * 19349663 ^ opt.Seed * 2654435761 ^ 0x00B0BB1E);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                if ((h & 0xFFFFFFu) / 16777216f >= opt.Chance) continue;

                yield return (new Vector3((gx + 0.5f) * cellSize, EmitFloor, (gy + 0.5f) * cellSize), h);
            }
        }
    }

    private static float Frac(float x) => x - MathF.Floor(x);
    private static float Smoothstep(float t) { t = Math.Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }

    private static SpectralData FlatSpectrum(float value)
    {
        const int min = 360, max = 830, step = 5;
        int count = (max - min) / step + 1;
        var wavelengths = new int[count];
        var values = new float[count];
        for (int i = 0; i < count; i++) { wavelengths[i] = min + i * step; values[i] = value; }
        return new SpectralData(wavelengths, values, (float[])values.Clone());
    }
}
