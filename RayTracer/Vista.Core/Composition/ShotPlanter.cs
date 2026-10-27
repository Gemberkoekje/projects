using Vista.Astronomy;
using Vista.Scene;
using Vista.World;

namespace Vista.Composition;

/// <summary>
/// The shot-first plant of a mountain set piece (vista-plan §4.2, §10 M1.14): what the picture is of and where it is seen from, before
/// the auto camera refines it.
/// </summary>
public sealed record PlantedShot
{
    /// <summary>What the composition is about.</summary>
    public required CompositionIntent Intent { get; init; }

    /// <summary>The main peak: the highest summit of the main massif, m above the world's height 0.</summary>
    public required WorldPoint MainPeak { get; init; }

    /// <summary>The main massif's centre: the highest ground smoothed over 1.5 km, on the ground.</summary>
    public required WorldPoint Massif { get; init; }

    /// <summary>The valley floor around the main peak (the 10th percentile of the ground within 8 km), m.</summary>
    public required double FloorMetres { get; init; }

    /// <summary>The main peak's height above that floor, m.</summary>
    public double ReliefMetres => MainPeak.Y - FloorMetres;

    /// <summary>The range's axis: the compass bearing (0–180°) of the massif's high ground's long axis.</summary>
    public required double RangeAxisDegrees { get; init; }

    /// <summary>Half the range's length along its axis, m.</summary>
    public required double RangeHalfLengthMetres { get; init; }

    /// <summary>What the camera looks at: the main peak for a peak, the massif for a range.</summary>
    public WorldPoint Subject => Intent == CompositionIntent.Range ? Massif : MainPeak;

    /// <summary>The planted viewpoint, on the ground there (m above the world's height 0).</summary>
    public required WorldPoint Viewpoint { get; init; }

    /// <summary>The compass heading from the viewpoint toward the subject, degrees.</summary>
    public required double HeadingDegrees { get; init; }

    /// <summary>How far the viewpoint stands from the subject, horizontally, m.</summary>
    public required double DistanceMetres { get; init; }

    /// <summary>The sun's azimuth from that heading, folded into 0–180° (0: looking into the sun; 90: side light; 180: the sun behind).</summary>
    public required double SunRelativeDegrees { get; init; }

    /// <summary>The plant's quick framing score (<see cref="ShotPlanter"/>).</summary>
    public required double Score { get; init; }
}

/// <summary>
/// Plants the shot of a mountain set piece on a finished world (vista-plan §4.2, §10 M1.14): the world does not change (§4.1), so the
/// plant finds its main massif and main peak, the range's axis and the valley floor, and chooses a heading toward the subject with the
/// sun's azimuth (side or back light) and a viewpoint across from it at a distance where the subject fills a mid lens.
/// <list type="bullet">
///   <item><b>The massif and the peak</b>: the world on a 128 m grid, smoothed by a Gaussian of 1.5 km; its highest point in the
///   world's inner 64% (so a viewpoint fits in front of it) is the massif, the highest sample within 2.5 km of it the main peak.</item>
///   <item><b>The range's axis</b>: the long axis (the second moments) of the high ground (the top quarter of the smoothed heights)
///   within 8 km of the massif.</item>
///   <item><b>The heading</b>: for a peak, the sun at 40°, 60°, 90° or 135° either side of it; for a range, broadside to the axis
///   from either side and 20° and 40° off it (an oblique view still holds the range, and gives the sun more ways to fall). Each is
///   tried at a distance (a peak: four times its relief, 4–12 km; a range: five times, and
///   enough to hold its length in a 35 mm frame, 6–16 km; shortened to fit in the world), 60 m above the ground there, framed on the
///   subject and scored (<see cref="CompositionScorer"/>, its buffer at 96×54): the best is the plant.</item>
/// </list>
/// Deterministic: a function of the world's heights and the sun.
/// </summary>
public static class ShotPlanter
{
    /// <summary>The planting grid's cell in world samples (128 m on the default grid).</summary>
    public const int CoarseFactor = 16;

    /// <summary>The massif map's smoothing, m (a Gaussian's σ).</summary>
    public const double MassifSigmaMetres = 1500.0;

    /// <summary>How high above the ground a planted viewpoint stands, m.</summary>
    public const double PlantHeightMetres = 60.0;

    /// <summary>The margin a viewpoint keeps from the world's edge, m.</summary>
    public const double EdgeMarginMetres = 1000.0;

    /// <summary>Plants the shot of <paramref name="intent"/> on <paramref name="sight"/>'s world under <paramref name="sun"/>.</summary>
    public static PlantedShot Plant(TerrainSight sight, SunPosition sun, CompositionIntent intent, CompositionWeights weights, CompositionLimits limits,
        double aspect)
    {
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(limits);
        if (intent is not (CompositionIntent.Peak or CompositionIntent.Range))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The auto camera plants a peak or a range.");
        HeightField world = sight.World;
        int step = Math.Max(1, Math.Min(CoarseFactor, (world.Size - 1) / 32));
        int n = (world.Size - 1) / step + 1;
        double cell = world.CellSize * step;
        var coarse = new float[n * n];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
                coarse[j * n + i] = world[i * step, j * step];
        }
        float[] massif = Smooth(coarse, n, MassifSigmaMetres / cell);

        // The massif: the smoothed map's highest point well inside the world.
        double inner = 0.32 * world.Extent;
        int best = -1;
        for (int k = 0; k < n * n; k++)
        {
            double x = world.OriginX + (k % n) * cell, z = world.OriginZ + (k / n) * cell;
            if (Math.Abs(x) <= inner && Math.Abs(z) <= inner && (best < 0 || massif[k] > massif[best]))
                best = k;
        }
        double mx = world.OriginX + (best % n) * cell, mz = world.OriginZ + (best / n) * cell;

        // The main peak: the highest sample within 2.5 km of it.
        (int pi, int pj) = (0, 0);
        float peak = float.NegativeInfinity;
        int reach = (int)Math.Ceiling(2500.0 / world.CellSize);
        int ci = (int)Math.Round((mx - world.OriginX) / world.CellSize), cj = (int)Math.Round((mz - world.OriginZ) / world.CellSize);
        for (int j = Math.Max(0, cj - reach); j <= Math.Min(world.Size - 1, cj + reach); j++)
        {
            for (int i = Math.Max(0, ci - reach); i <= Math.Min(world.Size - 1, ci + reach); i++)
            {
                double dx = (i - ci) * world.CellSize, dz = (j - cj) * world.CellSize;
                if (dx * dx + dz * dz <= 2500.0 * 2500.0 && world[i, j] > peak)
                    (peak, pi, pj) = (world[i, j], i, j);
            }
        }
        var mainPeak = new WorldPoint(world.XOf(pi), peak, world.ZOf(pj));

        // The floor around it, and the range's axis from the high ground around the massif.
        var around = new List<float>();
        var high = new List<(double X, double Z, float H)>();
        for (int k = 0; k < n * n; k++)
        {
            double x = world.OriginX + (k % n) * cell, z = world.OriginZ + (k / n) * cell;
            double dp = Math.Sqrt((x - mainPeak.X) * (x - mainPeak.X) + (z - mainPeak.Z) * (z - mainPeak.Z));
            if (dp <= 8000.0)
                around.Add(coarse[k]);
            double dm = Math.Sqrt((x - mx) * (x - mx) + (z - mz) * (z - mz));
            if (dm <= 8000.0)
                high.Add((x, z, massif[k]));
        }
        around.Sort();
        double floor = around[around.Count / 10];
        (double axis, double halfLength) = Axis(high);
        var massifPoint = new WorldPoint(mx, sight.GroundAt(mx, mz), mz);

        var shot = new PlantedShot
        {
            Intent = intent,
            MainPeak = mainPeak,
            Massif = massifPoint,
            FloorMetres = floor,
            RangeAxisDegrees = axis,
            RangeHalfLengthMetres = halfLength,
            Viewpoint = default,
            HeadingDegrees = 0.0,
            DistanceMetres = 0.0,
            SunRelativeDegrees = 0.0,
            Score = double.NegativeInfinity,
        };
        double relief = Math.Max(300.0, mainPeak.Y - floor);
        double[] headings = intent == CompositionIntent.Peak
            ? [.. new[] { 40.0, 60.0, 90.0, 135.0 }.SelectMany(a => new[] { sun.AzimuthDegrees + a, sun.AzimuthDegrees - a })]
            : [.. new[] { 90.0, 270.0 }.SelectMany(a => new[] { 0.0, -20.0, 20.0, -40.0, 40.0 }.Select(o => axis + a + o))];
        double distance = intent == CompositionIntent.Peak
            ? Math.Clamp(4.0 * relief, 4000.0, 12000.0)
            : Math.Clamp(Math.Max(5.0 * relief, 1.6 * halfLength), 6000.0, 16000.0);
        PlantedShot? chosen = null;
        foreach (double raw in headings)
        {
            double heading = Wrap(raw);
            WorldPoint subject = shot.Subject;
            (double sh, double ch) = Math.SinCos(heading * (Math.PI / 180.0));
            for (double d = distance; d >= 3000.0; d *= 0.8)
            {
                double vx = subject.X - sh * d, vz = subject.Z - ch * d;
                if (!InWorld(world, vx, vz, EdgeMarginMetres))
                    continue;
                var viewpoint = new WorldPoint(vx, sight.GroundAt(vx, vz), vz);
                var eye = new WorldPoint(vx, viewpoint.Y + PlantHeightMetres, vz);
                if (!sight.Sees(eye, mainPeak, 100.0 + 0.01 * d))
                    continue;
                CameraSpec camera = AutoCamera.Frame(sight, eye, mainPeak, subject, intent == CompositionIntent.Peak ? 50.0 : 35.0, 0.0, 0.22, aspect);
                CompositionBuffer buffer = sight.Trace(camera, 96, (int)Math.Round(96 / aspect), parallel: true);
                var context = new FramingContext(camera, sun, intent, mainPeak, d);
                double score = CompositionScorer.Score(buffer, context, limits, sight).Total(weights);
                if (chosen is null || score > chosen.Score)
                {
                    chosen = shot with
                    {
                        Viewpoint = viewpoint,
                        HeadingDegrees = heading,
                        DistanceMetres = d,
                        SunRelativeDegrees = Math.Abs(((sun.AzimuthDegrees - heading) % 360.0 + 540.0) % 360.0 - 180.0),
                        Score = score,
                    };
                }
                break;
            }
        }
        return chosen ?? throw new InvalidOperationException("No viewpoint in the world sees the main peak: the world is too small for a mountain shot.");
    }

    /// <summary>A compass bearing in [0, 360).</summary>
    public static double Wrap(double degrees) => ((degrees % 360.0) + 360.0) % 360.0;

    private static bool InWorld(HeightField world, double x, double z, double margin) =>
        x >= world.OriginX + margin && z >= world.OriginZ + margin && x <= world.OriginX + world.Extent - margin && z <= world.OriginZ + world.Extent - margin;

    /// <summary>
    /// The long axis of the high ground among <paramref name="cells"/> (those in the top quarter of their heights, weighted by how far
    /// above the quarter they stand): its compass bearing in [0, 180) and its half-length (twice the standard deviation along it), m.
    /// </summary>
    private static (double Degrees, double HalfLength) Axis(List<(double X, double Z, float H)> cells)
    {
        float[] heights = [.. cells.Select(c => c.H)];
        Array.Sort(heights);
        float cut = heights[(int)(0.75 * (heights.Length - 1))];
        double sw = 0.0, sx = 0.0, sz = 0.0;
        foreach ((double x, double z, float h) in cells)
        {
            if (h <= cut)
                continue;
            double weight = h - cut;
            (sw, sx, sz) = (sw + weight, sx + weight * x, sz + weight * z);
        }
        if (!(sw > 0.0))
            return (0.0, 2000.0);
        (double cx, double cz) = (sx / sw, sz / sw);
        double xx = 0.0, zz = 0.0, xz = 0.0;
        foreach ((double x, double z, float h) in cells)
        {
            if (h <= cut)
                continue;
            double weight = (h - cut) / sw;
            (xx, zz, xz) = (xx + weight * (x - cx) * (x - cx), zz + weight * (z - cz) * (z - cz), xz + weight * (x - cx) * (z - cz));
        }
        // The larger eigenvalue of [[xx, xz], [xz, zz]] and its eigenvector's bearing (atan2(east, north)).
        double mean = 0.5 * (xx + zz), diff = 0.5 * (xx - zz);
        double major = mean + Math.Sqrt(diff * diff + xz * xz);
        double angle = 0.5 * Math.Atan2(2.0 * xz, xx - zz); // from +X (east) toward +Z (north)
        double bearing = Wrap(90.0 - angle * (180.0 / Math.PI)) % 180.0;
        return (bearing, Math.Max(1000.0, 2.0 * Math.Sqrt(Math.Max(major, 0.0))));
    }

    /// <summary>A separable Gaussian blur of the <paramref name="n"/>² grid <paramref name="values"/> with σ = <paramref name="sigma"/> cells, the edges clamped.</summary>
    private static float[] Smooth(float[] values, int n, double sigma)
    {
        int radius = Math.Max(1, (int)Math.Ceiling(3.0 * sigma));
        var kernel = new double[2 * radius + 1];
        for (int k = -radius; k <= radius; k++)
            kernel[k + radius] = Math.Exp(-0.5 * k * k / (sigma * sigma));
        double norm = kernel.Sum();
        var across = new float[n * n];
        var result = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                double sum = 0.0;
                for (int k = -radius; k <= radius; k++)
                    sum += kernel[k + radius] * values[j * n + Math.Clamp(i + k, 0, n - 1)];
                across[j * n + i] = (float)(sum / norm);
            }
        });
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                double sum = 0.0;
                for (int k = -radius; k <= radius; k++)
                    sum += kernel[k + radius] * across[Math.Clamp(j + k, 0, n - 1) * n + i];
                result[j * n + i] = (float)(sum / norm);
            }
        });
        return result;
    }
}
