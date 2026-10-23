namespace Vista.World;

/// <summary>
/// The forest's detail below the world grid (vista-plan §10 M1.12b): the crowns of its canopy, so a forest edge on a ridge shows a
/// bumpy silhouette and a slope of forest near the camera is lumpy rather than smooth. One crown per lattice cell
/// (<see cref="CrownSpacingMetres"/>, the spacing of a stand's dominant trees and their groups), at a jittered spot: a dome of its own
/// height and width, a cone rounded over its whole radius (<see cref="Apex"/>); the crowns meet each other and the gaps between them
/// in wide, rounded creases (a smooth maximum), and the gaps lie at the ground. Conifers' pointed crowns, and higher ones, were tried:
/// their curvature refined every slope of forest within a few kilometres to half-metre triangles, and a ridge of forest 2 km off at
/// 4K took 78 M triangles where one acceleration structure takes 48 M (vista-log/M1.12b.md); the domes' relief, 2 m, still bumps a
/// skyline by a few pixels there.
/// Centred on its mean, so the forest does not stand proud of the open ground around it: the canopy's own height above the ground
/// is not in the mesh (its edge would be a wall the weights' bound could not hold), only its relief. Close up it is still not trees
/// (M5's, which keeps this beyond the distance where its trees take over).
/// </summary>
public sealed class CanopyDetail : ISurfaceDetail
{
    /// <summary>The distance between crowns, m: the lattice's cell.</summary>
    public const double CrownSpacingMetres = 8.0;

    /// <summary>The tallest crown's height over the gaps, in units of <see cref="CrownSpacingMetres"/>, by default: 2 m.</summary>
    public const double DefaultHeightRatio = 0.25;

    /// <summary>The rounding of a crown's apex, in units of its radius: over all of it, a dome.</summary>
    private const double Apex = 1.0;

    /// <summary>The width of the rounded crease where two crowns, or a crown and the gap, meet, in units of the tallest crown.</summary>
    private const double Join = 0.6;

    /// <summary>
    /// How far out, in units of its radius, a crown is evaluated: there it lies more than <see cref="Join"/> below the gap floor
    /// (0.69 below it at 1.6 radii of the shortest crown), so leaving it out moves no smooth maximum. A crown reaches at most
    /// 0.85 + 1.6 × 0.7 = 1.97 cells from its cell's corner, inside the 5×5 cells.
    /// </summary>
    private const double Reach = 1.6;

    /// <summary>The mean of the crowns before they are centred (measured over 4 M points).</summary>
    internal const double CrownMean = 0.4385;

    /// <summary>
    /// The largest |<see cref="Unit"/>|: the tallest crown (1, and the smooth maximum's rounding at most a quarter of
    /// <see cref="Join"/> above it) less <see cref="CrownMean"/>; a gap lies only <see cref="CrownMean"/> below.
    /// </summary>
    public const double UnitMax = 1.0 + 0.25 * Join - CrownMean;

    private static readonly double RoundedOne = Math.Sqrt(1.0 + Apex * Apex) - Apex;

    private readonly ulong _seed;

    /// <summary>The canopy detail of a world whose seed is <paramref name="worldSeed"/>.</summary>
    public CanopyDetail(ulong worldSeed, double heightRatio = DefaultHeightRatio)
    {
        if (!double.IsFinite(heightRatio) || heightRatio < 0.0)
            throw new ArgumentOutOfRangeException(nameof(heightRatio), heightRatio, "The height ratio must be finite and not negative.");
        _seed = StableHash.Combine(SeedDerivation.Derive(worldSeed, SeedDerivation.TerrainMaterials), 0xC4A0ul);
        HeightRatio = heightRatio;
    }

    /// <summary>
    /// The measured envelope of <see cref="Unit"/> (2 M random segments at each of eleven lengths, ×1.15;
    /// <see cref="DetailEnvelope.Measure"/>), below its first length the curvature bound with the crowns' largest second derivative
    /// measured over 4 M points (37.6, at the creases where two crowns meet), ×1.15, held where it dipped at four units. About a
    /// crown's height from one lattice unit up: a segment that long can step from a crown's top into a gap.
    /// </summary>
    public static DetailEnvelope Envelope { get; } = new(
    [
        0.00390625, 0.0000859, 0.0078125, 0.000269, 0.015625, 0.00112, 0.03125, 0.003994, 0.0625, 0.015426, 0.125, 0.053938, 0.25, 0.18446,
        0.5, 0.49350, 1.0, 0.94450, 2.0, 1.14481, 4.0, 1.14481,
    ], 43.3);

    /// <summary>The tallest crown's height over the gaps, in units of <see cref="CrownSpacingMetres"/>.</summary>
    public double HeightRatio { get; }

    /// <inheritdoc />
    public bool IsNone => HeightRatio == 0.0;

    /// <inheritdoc />
    public double MaxAmplitude => HeightRatio * CrownSpacingMetres * UnitMax;

    /// <summary>
    /// The crowns in lattice units: of the crowns of the 5×5 cells around the point, the smooth maximum of their heights there and
    /// the gap floor at 0, centred. A crown's height falls from its apex (rounded) along a cone to 0 at its radius and below it
    /// outside.
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
                double cx = fx + di + 0.15 + 0.7 * Fraction(h, 0), cy = fy + dj + 0.15 + 0.7 * Fraction(h, 21);
                double radius = 0.5 + 0.2 * Fraction(h, 42);
                double height = 0.6 + 0.4 * Fraction(StableHash.Mix(h), 0);
                double dx = x - cx, dy = y - cy;
                double q = Math.Sqrt(dx * dx + dy * dy) / radius;
                // Past Reach a crown lies more than the crease's width below the gap floor, where it changes nothing.
                if (q > Reach)
                    continue;
                double crown = height * (1.0 - (Math.Sqrt(q * q + Apex * Apex) - Apex) / RoundedOne);
                top = SmoothMax(top, crown, Join);
            }
        }
        return top - CrownMean;
    }

    /// <inheritdoc />
    public double Height(double x, double z)
    {
        double f = 1.0 / CrownSpacingMetres;
        return HeightRatio * CrownSpacingMetres * Unit(_seed, x * f, z * f);
    }

    /// <inheritdoc />
    public double MidpointBound(double length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return HeightRatio * CrownSpacingMetres * Envelope.At(length / CrownSpacingMetres);
    }

    /// <summary>A polynomial smooth maximum: max(a, b) where they differ by more than <paramref name="k"/>, rounded within it.</summary>
    private static double SmoothMax(double a, double b, double k)
    {
        double h = Math.Max(k - Math.Abs(a - b), 0.0) / k;
        return Math.Max(a, b) + h * h * k * 0.25;
    }

    private static double Fraction(ulong h, int shift) => ((h >> shift) & 0x1FFFFF) * (1.0 / 0x200000);
}
