namespace Vista.World;

/// <summary>
/// Bare rock's detail below the world grid (vista-plan §10 M1.12): ridged noise, so rock near the camera breaks into sharp crests
/// and edges where M1.9's smooth noise read as sand drifts in the 4K loupes. Each octave folds <see cref="GradientNoise"/> about
/// zero (<see cref="Unit"/>: one less twice its rounded absolute value), which turns the noise's zero lines into crests; the
/// rounding (<see cref="Crease"/>) keeps a crest a few hundredths of the octave's wavelength wide, which bounds its curvature and so
/// the mesh's error near the camera without blunting it where it shows. The octaves are laid out like <see cref="TerrainDetail"/>'s —
/// halving from the grid's cell down, each turned like the base terrain's — on a stream of their own, each
/// <see cref="AmplitudeRatio"/> of its wavelength high.
/// </summary>
public sealed class RockDetail : ISurfaceDetail
{
    /// <summary>The rounding of a crest, in units of the noise's value.</summary>
    public const double Crease = 0.12;

    /// <summary>
    /// Octaves by default: from the grid's cell (8 m) down to a quarter of it (2 m). Finer crests set the mesh's finest triangles over
    /// every rock face near the camera (at 4K, 1 m and 0.5 m octaves nearly tripled its triangles, vista-log/M1.12.md); below 2 m the
    /// shading's bump carries the rock's roughness instead (<see cref="MaterialRules.RoughRock"/>).
    /// </summary>
    public const int DefaultOctaves = 3;

    /// <summary>Amplitude over wavelength of every octave by default.</summary>
    public const double DefaultAmplitudeRatio = 0.05;

    /// <summary>The mean of the folded noise before it is centred (measured over 4 M points), so rock does not stand proud of the ground around it.</summary>
    internal const double FoldMean = 0.6176;

    /// <summary>The largest |<see cref="Unit"/>|: its lowest possible value, −1 less <see cref="FoldMean"/> (the folded noise lies in [−0.89, 1]).</summary>
    public const double UnitMax = 1.0 + FoldMean;

    private static readonly double RoundedOne = Math.Sqrt(1.0 + Crease * Crease) - Crease;

    private readonly ulong _seed;

    /// <summary>The rock detail of a world whose seed is <paramref name="worldSeed"/>, on a grid of <paramref name="cellSize"/> metres.</summary>
    public RockDetail(ulong worldSeed, double cellSize, int octaves = DefaultOctaves, double amplitudeRatio = DefaultAmplitudeRatio)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        ArgumentOutOfRangeException.ThrowIfNegative(octaves);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(octaves, 16);
        if (!double.IsFinite(amplitudeRatio) || amplitudeRatio < 0.0)
            throw new ArgumentOutOfRangeException(nameof(amplitudeRatio), amplitudeRatio, "The amplitude ratio must be finite and not negative.");
        _seed = StableHash.Combine(SeedDerivation.Derive(worldSeed, SeedDerivation.TerrainMaterials), 0x0C0Cul);
        CoarsestWavelength = cellSize;
        Octaves = amplitudeRatio > 0.0 ? octaves : 0;
        AmplitudeRatio = Octaves > 0 ? amplitudeRatio : 0.0;
    }

    /// <summary>
    /// The measured envelope of <see cref="Unit"/> (2 M random segments at each of eleven lengths, ×1.15;
    /// <see cref="DetailEnvelope.Measure"/>), below its first length the curvature bound with the folded noise's largest second
    /// derivative measured over 4 M points (149.8), ×1.15. Ten to twelve times the smooth noise's below a tenth of a wavelength: the
    /// price of the crests, in triangles near the camera.
    /// </summary>
    public static DetailEnvelope Envelope { get; } = new(
    [
        0.00390625, 0.00033, 0.0078125, 0.00137, 0.015625, 0.00538, 0.03125, 0.02107, 0.0625, 0.07308, 0.125, 0.24439, 0.25, 0.59933,
        0.5, 1.21569, 1.0, 1.78759, 2.0, 2.07733, 4.0, 2.12073,
    ], 173.0);

    /// <summary>Wavelength of the coarsest octave, m: the grid's cell (the grid itself holds the rock's shape above it).</summary>
    public double CoarsestWavelength { get; }

    /// <summary>Number of octaves (0: no detail).</summary>
    public int Octaves { get; }

    /// <summary>Amplitude of an octave over its wavelength.</summary>
    public double AmplitudeRatio { get; }

    /// <inheritdoc />
    public bool IsNone => Octaves == 0;

    /// <inheritdoc />
    public double MaxAmplitude
    {
        get
        {
            double sum = 0.0, wavelength = CoarsestWavelength;
            for (int k = 0; k < Octaves; k++, wavelength *= 0.5)
                sum += wavelength;
            return AmplitudeRatio * UnitMax * sum;
        }
    }

    /// <summary>One octave in lattice units: <see cref="GradientNoise"/> folded about zero into crests, centred.</summary>
    public static double Unit(ulong seed, double x, double y)
    {
        double n = GradientNoise.Sample(seed, x, y);
        return 1.0 - 2.0 * (Math.Sqrt(n * n + Crease * Crease) - Crease) / RoundedOne - FoldMean;
    }

    /// <inheritdoc />
    public double Height(double x, double z)
    {
        double sum = 0.0;
        double wavelength = CoarsestWavelength;
        double px = x, pz = z;
        for (int k = 0; k < Octaves; k++)
        {
            double f = 1.0 / wavelength;
            sum += AmplitudeRatio * wavelength * Unit(StableHash.Combine(_seed, (ulong)k), px * f, pz * f);
            wavelength *= 0.5;
            (px, pz) = (0.8 * px - 0.6 * pz, 0.6 * px + 0.8 * pz);
        }
        return sum;
    }

    /// <inheritdoc />
    public double MidpointBound(double length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        double sum = 0.0, wavelength = CoarsestWavelength;
        for (int k = 0; k < Octaves; k++, wavelength *= 0.5)
            sum += AmplitudeRatio * wavelength * Envelope.At(length / wavelength);
        return sum;
    }
}
