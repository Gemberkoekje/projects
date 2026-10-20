namespace Vista.World;

/// <summary>
/// The terrain's detail below the world grid (vista-plan §4.1, §10 M1.9): seeded fractal noise added to the surface everywhere,
/// a pure function of position, so a camera near the ground sees rock and rubble finer than the grid's cells and a camera far
/// away sees exactly the same world with that detail too small to matter. Its octaves start at twice the grid's cell (the
/// finest wavelength the grid itself can hold) and halve <see cref="Octaves"/> times; octave <c>k</c> has wavelength
/// <c>λ_k</c> and amplitude <see cref="AmplitudeRatio"/>·<c>λ_k</c> (self-similar, like real rock surfaces), and each turns
/// the sampling domain like the base terrain's octaves, so no lattice lines up.
///
/// <para>The view-dependent mesh needs to know how far a vertex may stray from the straight edge it splits, at each level of
/// detail, without looking (<see cref="MidpointBound"/>). Per octave that is how far <see cref="GradientNoise"/> at a segment's
/// midpoint can lie from the mean of its ends, for a segment <c>t</c> lattice units long: below a quarter of a unit the curvature
/// bound <c>C₂t²/8</c> (with <see cref="NoiseCurvatureMax"/>, tight there), above it <see cref="Envelope"/>, the worst deviation
/// measured over 2 M random segments at each of nine lengths with a 15% margin. The provable bound from the noise's largest
/// gradient and value was up to 1.8 times looser near a unit, which cost the mesh two to three times the triangles below the
/// grid; a test pins the envelope over fresh segments.</para>
/// </summary>
public sealed class TerrainDetail
{
    /// <summary>The largest |<see cref="GradientNoise.Sample"/>|: √2·√(2/4) for unit gradients (0.934 measured).</summary>
    public const double NoiseMax = 1.0;

    /// <summary>A bound on the noise's second directional derivative in lattice units (12.67 measured over 4 M points).</summary>
    public const double NoiseCurvatureMax = 14.0;

    /// <summary>The detail the M1.9 mesh adds: six octaves from twice the grid's cell, each 4% of its wavelength high.</summary>
    public const int DefaultOctaves = 6;

    /// <summary>Amplitude over wavelength of every octave by default.</summary>
    public const double DefaultAmplitudeRatio = 0.04;

    private readonly ulong _seed;

    /// <summary>
    /// The detail of a world whose seed is <paramref name="worldSeed"/>, on a grid of <paramref name="cellSize"/> metres:
    /// <paramref name="octaves"/> octaves from <c>2·cellSize</c> down, each <paramref name="amplitudeRatio"/> of its wavelength
    /// high. Zero octaves or a zero ratio is no detail.
    /// </summary>
    public TerrainDetail(ulong worldSeed, double cellSize, int octaves = DefaultOctaves, double amplitudeRatio = DefaultAmplitudeRatio)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        ArgumentOutOfRangeException.ThrowIfNegative(octaves);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(octaves, 16);
        if (!double.IsFinite(amplitudeRatio) || amplitudeRatio < 0.0)
            throw new ArgumentOutOfRangeException(nameof(amplitudeRatio), amplitudeRatio, "The amplitude ratio must be finite and not negative.");
        _seed = SeedDerivation.Derive(worldSeed, SeedDerivation.TerrainDetail);
        CoarsestWavelength = 2.0 * cellSize;
        Octaves = amplitudeRatio > 0.0 ? octaves : 0;
        AmplitudeRatio = Octaves > 0 ? amplitudeRatio : 0.0;
    }

    /// <summary>No detail at all.</summary>
    public static TerrainDetail None { get; } = new(0, 1.0, 0, 0.0);

    /// <summary>Wavelength of the coarsest octave, m: twice the grid's cell.</summary>
    public double CoarsestWavelength { get; }

    /// <summary>Wavelength of the finest octave, m.</summary>
    public double FinestWavelength => CoarsestWavelength / Math.Pow(2.0, Math.Max(Octaves - 1, 0));

    /// <summary>Number of octaves (0: no detail).</summary>
    public int Octaves { get; }

    /// <summary>Amplitude of an octave over its wavelength.</summary>
    public double AmplitudeRatio { get; }

    /// <summary>True when the detail adds nothing.</summary>
    public bool IsNone => Octaves == 0;

    /// <summary>The most the detail can raise or lower a point, m.</summary>
    public double MaxAmplitude => AmplitudeRatio * NoiseMax * SumOfWavelengths();

    /// <summary>The detail's height at world <c>(x, z)</c>, m: a pure function of the position and the world's seed.</summary>
    public double Height(double x, double z)
    {
        double sum = 0.0;
        double wavelength = CoarsestWavelength;
        double px = x, pz = z;
        for (int k = 0; k < Octaves; k++)
        {
            double f = 1.0 / wavelength;
            sum += AmplitudeRatio * wavelength * GradientNoise.Sample(StableHash.Combine(_seed, (ulong)k), px * f, pz * f);
            wavelength *= 0.5;
            // The base terrain's exact rational turn (0.8, 0.6) between octaves, so no two lattices line up.
            (px, pz) = (0.8 * px - 0.6 * pz, 0.6 * px + 0.8 * pz);
        }
        return sum;
    }

    /// <summary>
    /// How far the detail at the midpoint of a straight segment <paramref name="length"/> metres long can lie from the mean of the
    /// detail at its ends: per octave <see cref="UnitMidpointBound"/> of the length in that octave's lattice units, times its
    /// amplitude. It falls with the length (quadratically once the segment is shorter than the finest octave), which keeps the
    /// mesh's levels of detail nested.
    /// </summary>
    public double MidpointBound(double length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        double sum = 0.0;
        double wavelength = CoarsestWavelength;
        for (int k = 0; k < Octaves; k++)
        {
            sum += AmplitudeRatio * wavelength * UnitMidpointBound(length / wavelength);
            wavelength *= 0.5;
        }
        return sum;
    }

    /// <summary>
    /// The worst midpoint deviation of <see cref="GradientNoise"/> over segments <c>t</c> lattice units long, as measured (2 M random
    /// segments at each length, seeds and directions random), times 1.15: <c>(t, bound)</c>, rising, flat past the last.
    /// </summary>
    internal static ReadOnlySpan<double> Envelope =>
    [
        0.25, 0.1084, 0.35355, 0.2077, 0.5, 0.3786, 0.70711, 0.6336, 1.0, 0.9800,
        1.41421, 1.4043, 2.0, 1.7008, 2.82843, 1.9359,
    ];

    /// <summary>
    /// How far the noise at a segment's midpoint can lie from the mean of its ends, for a segment <paramref name="t"/> lattice units
    /// long: the curvature bound below a quarter of a unit, <see cref="Envelope"/> (interpolated, never below the curvature bound's
    /// value at its first point) above. Never decreasing in <paramref name="t"/>.
    /// </summary>
    public static double UnitMidpointBound(double t)
    {
        ReadOnlySpan<double> e = Envelope;
        if (t <= e[0])
            return Math.Min(NoiseCurvatureMax * t * t / 8.0, e[1]);
        for (int k = 2; k < e.Length; k += 2)
        {
            if (t <= e[k])
                return e[k - 1] + (e[k + 1] - e[k - 1]) * (t - e[k - 2]) / (e[k] - e[k - 2]);
        }
        return e[^1];
    }

    private double SumOfWavelengths()
    {
        double sum = 0.0, wavelength = CoarsestWavelength;
        for (int k = 0; k < Octaves; k++, wavelength *= 0.5)
            sum += wavelength;
        return sum;
    }
}
