using Vista.Recipes;

namespace Vista.World;

/// <summary>
/// The base terrain shape a recipe's set pieces ask for (vista-plan §4.3): fractal noise parameters in metres.
/// M0 ships two shapes — ridged mountains and soft hills — with no erosion yet (M1's "realism lever"). When
/// pieces combine, mountains win: placing hills, coast and forest <i>around</i> a mountain backdrop is the
/// shot-first layout's job (§4.2), not M0's. Every other set — no pieces, or only hills, coast, forest and
/// water — gets the hills shape as plain base ground until those pieces' milestones.
/// </summary>
/// <param name="Name">Stable profile key, recorded in the sidecar.</param>
/// <param name="ReliefMetres">Height range from the lowest to the highest possible point.</param>
/// <param name="WavelengthMetres">Wavelength of the coarsest octave.</param>
/// <param name="Octaves">Number of noise octaves.</param>
/// <param name="Persistence">Amplitude ratio between successive octaves.</param>
/// <param name="Ridged">Ridged (sharp crests, rounded valleys) rather than plain fBm.</param>
/// <param name="WarpFraction">Domain-warp displacement as a fraction of <paramref name="WavelengthMetres"/>.</param>
public sealed record TerrainProfile(
    string Name,
    double ReliefMetres,
    double WavelengthMetres,
    int Octaves,
    double Persistence,
    bool Ridged,
    double WarpFraction = 0.35)
{
    /// <summary>Wavelength of the warp field relative to the coarsest octave.</summary>
    private const double WarpWavelengthFactor = 1.5;

    /// <summary>Ridged alpine relief for snow-capped mountains and alpine ranges.</summary>
    public static TerrainProfile Mountains { get; } = new("mountains", 2400.0, 6000.0, 9, 0.5, Ridged: true);

    /// <summary>Gentle relief for rolling hills (and, until their milestones, coast and forest).</summary>
    public static TerrainProfile Hills { get; } = new("hills", 220.0, 2500.0, 7, 0.5, Ridged: false);

    /// <summary>The profile for a recipe's set pieces (order-independent).</summary>
    public static TerrainProfile For(IEnumerable<SetPiece> pieces) =>
        pieces.Any(p => p is SetPiece.SnowCappedMountains or SetPiece.AlpineRange) ? Mountains : Hills;

    /// <summary>The mean of one ridged octave, <c>E[(1 − |n|)²]</c> of <see cref="GradientNoise"/> (measured over 32 M points).</summary>
    public const double RidgedOctaveMean = 0.5948;

    /// <summary>
    /// Terrain height in metres, in [0, <see cref="ReliefMetres"/>], at world <c>(x, z)</c> — a pure function
    /// of the position and <paramref name="seed"/>.
    /// </summary>
    public double Height(ulong seed, double x, double z) => Height(seed, x, z, Octaves);

    /// <summary>
    /// The terrain with only its first <paramref name="octaves"/> octaves (vista-plan §10 M1.9): the far field's coarse grids
    /// sample it, since finer octaves would alias there. Each octave left out is replaced by its mean, so the ground keeps its
    /// mean height; with every octave this is <see cref="Height(ulong, double, double)"/> bit for bit.
    /// </summary>
    public double Height(ulong seed, double x, double z, int octaves)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(octaves);
        octaves = Math.Min(octaves, Octaves);
        // Domain warp (§4.3): displace the sample point by two low-frequency noise fields, so the lattice
        // lines of every octave bend instead of running ruler-straight across the world.
        double wf = 1.0 / (WavelengthMetres * WarpWavelengthFactor);
        double warp = WarpFraction * WavelengthMetres;
        ulong warpSeed = StableHash.Combine(seed, WarpStream);
        double px = x + warp * GradientNoise.Sample(warpSeed, x * wf, z * wf);
        double pz = z + warp * GradientNoise.Sample(StableHash.Mix(warpSeed), x * wf, z * wf);

        double frequency = 1.0 / WavelengthMetres;
        double amplitude = 1.0;
        double sum = 0.0;
        double norm = 0.0;
        for (int octave = 0; octave < Octaves; octave++)
        {
            if (octave >= octaves)
            {
                // Left out: its mean instead (the shared tail of the loop keeps the norm and the amplitudes the same).
                sum += (Ridged ? RidgedOctaveMean : 0.5) * amplitude;
                norm += amplitude;
                amplitude *= Persistence;
                continue;
            }
            double n = GradientNoise.Sample(StableHash.Combine(seed, (ulong)octave), px * frequency, pz * frequency);
            if (Ridged)
            {
                double r = 1.0 - Math.Abs(n);
                sum += r * r * amplitude;
            }
            else
            {
                sum += (n * 0.5 + 0.5) * amplitude;
            }
            norm += amplitude;
            amplitude *= Persistence;
            frequency *= 2.0;

            // Rotate the sample domain by the exact rational rotation (0.8, 0.6) between octaves so lattice
            // axes never line up across octaves (no visible grid), using only multiplications and additions.
            (px, pz) = (0.8 * px - 0.6 * pz, 0.6 * px + 0.8 * pz);
        }
        return Math.Clamp(sum / norm, 0.0, 1.0) * ReliefMetres;
    }

    /// <summary>
    /// The terrain's large-scale shape (vista-plan §10 M1.11d's uplift map): its first <paramref name="octaves"/> octaves alone,
    /// normalised by their own weights to [0, 1] (no means stand in for the rest), at world <c>(x, z)</c> under the same warp
    /// and rotations as <see cref="Height(ulong, double, double, int)"/>, so its high ground is the terrain's massifs.
    /// </summary>
    public double Shape(ulong seed, double x, double z, int octaves)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(octaves);
        octaves = Math.Min(octaves, Octaves);
        double wf = 1.0 / (WavelengthMetres * WarpWavelengthFactor);
        double warp = WarpFraction * WavelengthMetres;
        ulong warpSeed = StableHash.Combine(seed, WarpStream);
        double px = x + warp * GradientNoise.Sample(warpSeed, x * wf, z * wf);
        double pz = z + warp * GradientNoise.Sample(StableHash.Mix(warpSeed), x * wf, z * wf);
        double frequency = 1.0 / WavelengthMetres;
        double amplitude = 1.0, sum = 0.0, norm = 0.0;
        for (int octave = 0; octave < octaves; octave++)
        {
            double n = GradientNoise.Sample(StableHash.Combine(seed, (ulong)octave), px * frequency, pz * frequency);
            double r = 1.0 - Math.Abs(n);
            sum += (Ridged ? r * r : n * 0.5 + 0.5) * amplitude;
            norm += amplitude;
            amplitude *= Persistence;
            frequency *= 2.0;
            (px, pz) = (0.8 * px - 0.6 * pz, 0.6 * px + 0.8 * pz);
        }
        return Math.Clamp(sum / norm, 0.0, 1.0);
    }

    /// <summary>
    /// The number of octaves a grid of <paramref name="cellSize"/> metres can hold: those whose wavelength is at least two
    /// cells (at least one).
    /// </summary>
    public int OctavesFor(double cellSize)
    {
        int octaves = 1;
        double wavelength = WavelengthMetres * 0.5;
        while (octaves < Octaves && wavelength >= 2.0 * cellSize)
        {
            octaves++;
            wavelength *= 0.5;
        }
        return octaves;
    }

    // Sub-stream key for the warp fields, distinct from every octave index.
    private const ulong WarpStream = 0x5741_5250_0000_0000ul; // "WARP"
}
