namespace Vista.World;

/// <summary>
/// Scree's detail below the world grid (vista-plan §10 M1.12): small angular stones on a gently uneven talus. Each octave scatters
/// one stone per lattice cell (<see cref="Unit"/>): a squat four-sided pyramid of its own size, height, proportions and turn, at a
/// jittered spot, its faces flat and its edges and apex slightly rounded (a superellipse of order 8 as its footprint); neighbouring
/// stones meet in a slightly rounded crease (a smooth maximum), and between them lies the ground. Under the stones runs half of
/// the smooth ground's detail, so a talus is not a plane. The roundings bound the stones' curvature, and so the mesh's error near
/// the camera; the mesh still shows them as angular wherever it resolves a stone.
/// </summary>
public sealed class ScreeDetail : ISurfaceDetail
{
    /// <summary>
    /// The wavelengths of the stones' octaves (one stone per lattice cell), m: stones of about a metre. A second octave at 0.6 m
    /// (stones 8 cm high) set the mesh's finest triangles over all the ground near the camera at 4K (vista-log/M1.12.md).
    /// </summary>
    public static IReadOnlyList<double> StoneWavelengths { get; } = [1.6];

    /// <summary>A stone's height over its cell by default.</summary>
    public const double DefaultStoneRatio = 0.14;

    /// <summary>The share of the smooth ground's detail under the stones.</summary>
    public const double GroundShare = 0.5;

    /// <summary>The rounding of a stone's apex, in lattice units.</summary>
    private const double Apex = 0.06;

    /// <summary>The width of the rounded crease where two stones meet, in units of their height.</summary>
    private const double Join = 0.12;

    /// <summary>
    /// How far out, in units of its size, a stone is evaluated: there it lies 0.34 of its height (at least 0.153) below its base,
    /// more than <see cref="Join"/> below the ground, so leaving it out moves no smooth maximum. It reaches at most 2.06 cells from
    /// its spot (1.4 × 2^⅜ for the superellipse's diagonal × 0.85 × 4/3 for its size and stretch), inside the 5×5 cells.
    /// </summary>
    private const double Reach = 1.4;

    /// <summary>The mean of the stones before they are centred (measured over 4 M points).</summary>
    internal const double StoneMean = 0.3966;

    /// <summary>The largest |<see cref="Unit"/>|: the highest stone less <see cref="StoneMean"/>, or the bare ground at −<see cref="StoneMean"/>.</summary>
    public const double UnitMax = 1.0 - StoneMean + 0.03;

    private readonly ulong _seed;
    private readonly TerrainDetail _ground;

    /// <summary>The scree detail of a world whose seed is <paramref name="worldSeed"/>, on a grid of <paramref name="cellSize"/> metres.</summary>
    public ScreeDetail(ulong worldSeed, double cellSize, double stoneRatio = DefaultStoneRatio)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        if (!double.IsFinite(stoneRatio) || stoneRatio < 0.0)
            throw new ArgumentOutOfRangeException(nameof(stoneRatio), stoneRatio, "The stone ratio must be finite and not negative.");
        _seed = StableHash.Combine(SeedDerivation.Derive(worldSeed, SeedDerivation.TerrainMaterials), 0x5C9Eul);
        StoneRatio = stoneRatio;
        _ground = new TerrainDetail(worldSeed, cellSize, TerrainDetail.DefaultOctaves, TerrainDetail.DefaultAmplitudeRatio * GroundShare);
    }

    /// <summary>
    /// The measured envelope of <see cref="Unit"/> (2 M random segments at each of eleven lengths, ×1.15, held where it dipped at two
    /// units; <see cref="DetailEnvelope.Measure"/>), below its first length the curvature bound with the stones' largest second
    /// derivative measured over 4 M points (198.6), ×1.15.
    /// </summary>
    public static DetailEnvelope Envelope { get; } = new(
    [
        0.00390625, 0.00034, 0.0078125, 0.00127, 0.015625, 0.00455, 0.03125, 0.01239, 0.0625, 0.03373, 0.125, 0.09772, 0.25, 0.27209,
        0.5, 0.5646, 1.0, 1.06272, 2.0, 1.06272, 4.0, 1.06728,
    ], 229.0);

    /// <summary>A stone's height over its cell.</summary>
    public double StoneRatio { get; }

    /// <inheritdoc />
    public bool IsNone => false;

    /// <inheritdoc />
    public double MaxAmplitude => StoneRatio * UnitMax * StoneWavelengths.Sum() + _ground.MaxAmplitude;

    /// <summary>
    /// One octave of stones in lattice units: of the stones of the 5×5 cells around the point (a stone reaches at most 2.06 cells
    /// from its spot), the smooth maximum of their heights
    /// there (a stone's height falls from its apex to 0 at its edge and below it outside), and at least the bare ground, centred.
    /// </summary>
    public static double Unit(ulong seed, double x, double y)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        long ix = (long)fx, iy = (long)fy;
        double top = 0.0;
        for (int dj = -2; dj <= 2; dj++)
        {
            for (int di = -2; di <= 2; di++)
            {
                ulong h = StableHash.Lattice(seed, ix + di, iy + dj);
                ulong g = StableHash.Mix(h);
                double cx = fx + di + 0.2 + 0.6 * Fraction(h, 0), cy = fy + dj + 0.2 + 0.6 * Fraction(h, 21);
                double size = 0.55 + 0.3 * Fraction(h, 42);
                double height = 0.45 + 0.55 * Fraction(g, 0);
                double stretch = 0.75 + 0.5 * Fraction(g, 21);
                // A turn without a transcendental function: the rational point ((1 − t²), 2t)/(1 + t²) on the circle.
                double t = 2.0 * Fraction(g, 42) - 1.0, c = (1.0 - t * t) / (1.0 + t * t), s = 2.0 * t / (1.0 + t * t);
                double u = (c * (x - cx) + s * (y - cy)) / (size * stretch), v = (c * (y - cy) - s * (x - cx)) * stretch / size;
                double u2 = u * u, v2 = v * v;
                double footprint = Math.Sqrt(Math.Sqrt(Math.Sqrt(u2 * u2 * u2 * u2 + v2 * v2 * v2 * v2)));
                // Past Reach a stone lies more than the crease's width below the ground, where it changes nothing; there it is
                // left out, so the 5×5 cells around a point hold every stone that can reach it.
                if (footprint > Reach)
                    continue;
                double stone = height * (1.0 - (Math.Sqrt(footprint * footprint + Apex * Apex) - Apex));
                top = SmoothMax(top, stone, Join);
            }
        }
        return top - StoneMean;
    }

    /// <inheritdoc />
    public double Height(double x, double z)
    {
        double sum = _ground.Height(x, z);
        double px = x, pz = z;
        for (int k = 0; k < StoneWavelengths.Count; k++)
        {
            double wavelength = StoneWavelengths[k], f = 1.0 / wavelength;
            sum += StoneRatio * wavelength * Unit(StableHash.Combine(_seed, (ulong)k), px * f, pz * f);
            (px, pz) = (0.8 * px - 0.6 * pz, 0.6 * px + 0.8 * pz);
        }
        return sum;
    }

    /// <inheritdoc />
    public double MidpointBound(double length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        double sum = _ground.MidpointBound(length);
        foreach (double wavelength in StoneWavelengths)
            sum += StoneRatio * wavelength * Envelope.At(length / wavelength);
        return sum;
    }

    /// <summary>A polynomial smooth maximum: max(a, b) where they differ by more than <paramref name="k"/>, rounded within it.</summary>
    private static double SmoothMax(double a, double b, double k)
    {
        double h = Math.Max(k - Math.Abs(a - b), 0.0) / k;
        return Math.Max(a, b) + h * h * k * 0.25;
    }

    private static double Fraction(ulong h, int shift) => ((h >> shift) & 0x1FFFFF) * (1.0 / 0x200000);
}
