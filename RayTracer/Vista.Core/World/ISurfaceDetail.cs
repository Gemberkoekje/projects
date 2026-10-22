namespace Vista.World;

/// <summary>
/// The terrain's detail below the world grid, as the view-dependent mesh sees it (vista-plan §10 M1.9, M1.12): a height added to
/// the surface everywhere, a pure function of position, and a bound on how far it can stray, at the midpoint of a straight edge
/// of any length, from the mean of the edge's ends — what the mesh refines by without looking. M1.9's detail is smooth noise
/// (<see cref="TerrainDetail"/>); since M1.12 the detail follows the ground's material (<see cref="MaterialDetail"/>).
/// </summary>
public interface ISurfaceDetail
{
    /// <summary>The detail's height at world <c>(x, z)</c>, m.</summary>
    double Height(double x, double z);

    /// <summary>
    /// How far the detail at the midpoint of a straight segment <paramref name="length"/> metres long can lie from the mean of the
    /// detail at its ends, m: never falling as the segment shrinks, so the mesh's levels of detail stay nested.
    /// </summary>
    double MidpointBound(double length);

    /// <summary>The most the detail can raise or lower a point, m.</summary>
    double MaxAmplitude { get; }

    /// <summary>True when the detail adds nothing.</summary>
    bool IsNone { get; }

    /// <summary>
    /// The bounds of the detail's kinds for a segment <paramref name="length"/> metres long, for <see cref="MidpointBoundIn"/> (vista-plan
    /// §10 M1.12); empty for a detail that is alike everywhere, whose bound is <see cref="MidpointBound(double)"/> wherever a segment lies.
    /// </summary>
    double[] KindBounds(double length) => [];

    /// <summary>
    /// <see cref="MidpointBound(double)"/> for a segment <paramref name="length"/> metres long that lies in the box from world
    /// <c>(x0, z0)</c> to <c>(x1, z1)</c>, given <see cref="KindBounds"/> at that length: never more than the bound everywhere, and
    /// never less in a box than in any box inside it.
    /// </summary>
    double MidpointBoundIn(double length, double[] kindBounds, double x0, double z0, double x1, double z1) => MidpointBound(length);
}

/// <summary>
/// A measured bound on how far a unit noise (one octave, in lattice units) at a segment's midpoint can lie from the mean of its
/// ends, for a segment <c>t</c> lattice units long (vista-plan §10 M1.9): below the first measured length the curvature bound
/// <c>C·t²/8</c>, above it the worst deviation measured over random segments, ×1.15, interpolated and flat past the last.
/// </summary>
/// <param name="Points">The measured envelope as <c>(t, bound)</c> pairs, both rising.</param>
/// <param name="CurvatureMax">A bound on the unit noise's second directional derivative, in lattice units.</param>
public sealed record DetailEnvelope(double[] Points, double CurvatureMax)
{
    /// <summary>The bound for a segment <paramref name="t"/> lattice units long: never decreasing in <paramref name="t"/>.</summary>
    public double At(double t)
    {
        double[] e = Points;
        if (t <= e[0])
            return Math.Min(CurvatureMax * t * t / 8.0, e[1]);
        for (int k = 2; k < e.Length; k += 2)
        {
            if (t <= e[k])
                return e[k - 1] + (e[k + 1] - e[k - 1]) * (t - e[k - 2]) / (e[k] - e[k - 2]);
        }
        return e[^1];
    }

    /// <summary>
    /// The worst midpoint deviation of <paramref name="unit"/> (a function of a seed and a position in lattice units) over
    /// <paramref name="segments"/> random segments at each of <paramref name="lengths"/>, random seeds, places and directions:
    /// how the envelopes were measured (a test checks them on fresh segments).
    /// </summary>
    public static double[] Measure(Func<ulong, double, double, double> unit, IReadOnlyList<double> lengths, int segments, int seed)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(lengths);
        var worst = new double[lengths.Count];
        Parallel.For(0, lengths.Count, l =>
        {
            var random = new Random(seed + 7919 * l);
            double t = lengths[l], max = 0.0;
            for (int i = 0; i < segments; i++)
            {
                ulong s = (ulong)random.NextInt64();
                double x = random.NextDouble() * 1000.0 - 500.0, y = random.NextDouble() * 1000.0 - 500.0, a = random.NextDouble() * 2.0 * Math.PI;
                double bx = x + t * Math.Cos(a), by = y + t * Math.Sin(a);
                max = Math.Max(max, Math.Abs(unit(s, 0.5 * (x + bx), 0.5 * (y + by)) - 0.5 * (unit(s, x, y) + unit(s, bx, by))));
            }
            worst[l] = max;
        });
        return worst;
    }

    /// <summary>
    /// The largest |second directional derivative| of <paramref name="unit"/> over <paramref name="points"/> random points (central
    /// differences, step 10⁻⁴), and the largest and the mean of its value: what <see cref="CurvatureMax"/> and a kind's amplitude
    /// were set from.
    /// </summary>
    public static (double Curvature, double Max, double Min, double Mean) MeasureShape(Func<ulong, double, double, double> unit, int points, int seed)
    {
        ArgumentNullException.ThrowIfNull(unit);
        var random = new Random(seed);
        const double h = 1e-4;
        double curvature = 0.0, max = double.MinValue, min = double.MaxValue, sum = 0.0;
        for (int i = 0; i < points; i++)
        {
            ulong s = (ulong)random.NextInt64();
            double x = random.NextDouble() * 1000.0 - 500.0, y = random.NextDouble() * 1000.0 - 500.0, a = random.NextDouble() * 2.0 * Math.PI;
            double n = unit(s, x, y);
            double dx = Math.Cos(a) * h, dy = Math.Sin(a) * h;
            curvature = Math.Max(curvature, Math.Abs(unit(s, x + dx, y + dy) - 2.0 * n + unit(s, x - dx, y - dy)) / (h * h));
            max = Math.Max(max, n);
            min = Math.Min(min, n);
            sum += n;
        }
        return (curvature, max, min, sum / points);
    }
}
