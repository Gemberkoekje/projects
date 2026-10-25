namespace Vista.World;

/// <summary>
/// What erodes a world (vista-plan §10 M1.7): the GPU in <c>Vista.App</c>, or <see cref="ReferenceEroder"/> (the C# reference,
/// for tests and small grids). Erosion is a global simulation, so it runs on the fixed world grid, never per camera (§4.1).
/// </summary>
public interface ITerrainEroder
{
    /// <summary>The device the erosion runs on, for the sidecar and the cache.</summary>
    string DeviceName { get; }

    /// <summary>The driver behind <see cref="DeviceName"/>, or empty when there is none to name.</summary>
    string DriverVersion { get; }

    /// <summary>
    /// Erodes <paramref name="ground"/> with <paramref name="settings"/>, its rock's hardness from <paramref name="strata"/> (on the
    /// ground's grid; without them every rock is the softest).
    /// </summary>
    ErosionRun Erode(HeightField ground, ErosionSettings settings, StrataGrid? strata = null);
}

/// <summary>One erosion: the world, and what it cost.</summary>
/// <param name="Result">The eroded world.</param>
/// <param name="Stats">Time and memory.</param>
public sealed record ErosionRun(ErosionResult Result, ErosionStats Stats);

/// <summary>What an erosion cost.</summary>
/// <param name="Seconds">Device time of the passes (GPU time from timestamp queries; wall time for the reference).</param>
/// <param name="WallSeconds">Wall time, including upload and readback.</param>
/// <param name="Submissions">Command lists submitted (each kept far below the Windows GPU timeout).</param>
/// <param name="LongestSubmissionSeconds">The longest submission's device time.</param>
/// <param name="StateBytes">Memory the simulation's state took on the device.</param>
public sealed record ErosionStats(double Seconds, double WallSeconds, int Submissions, double LongestSubmissionSeconds, long StateBytes)
{
    /// <summary>Video memory the whole process used while the state was allocated (DXGI), in bytes; 0 when not measured.</summary>
    public long ProcessVideoMemory { get; init; }

    /// <summary>
    /// Seconds the stream-power incision took on the CPU before the passes (<see cref="StreamPower"/>, M1.11a); 0 when the
    /// settings run none.
    /// </summary>
    public double IncisionSeconds { get; init; }

    /// <summary>Seconds the maps and the drainage took on the CPU after the passes (part of <see cref="WallSeconds"/>).</summary>
    public double MapsSeconds { get; init; }

    /// <summary>Seconds laying today's glaciers took on the CPU after the erosion (<see cref="Glaciers"/>, M1.13); 0 without them.</summary>
    public double GlacierSeconds { get; init; }
}

/// <summary>The C# reference as an <see cref="ITerrainEroder"/>: exact, but some 10⁴ times slower than the GPU, so for small grids.</summary>
public sealed class ReferenceEroder : ITerrainEroder
{
    /// <inheritdoc />
    public string DeviceName => "C# reference";

    /// <inheritdoc />
    public string DriverVersion => string.Empty;

    /// <inheritdoc />
    public ErosionRun Erode(HeightField ground, ErosionSettings settings, StrataGrid? strata = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var state = new ErosionState(ground, strata);
        ErosionReference.Run(state, settings, settings.Iterations);
        double seconds = stopwatch.Elapsed.TotalSeconds;
        ErosionResult result = ErosionResult.From(state, settings.PoolFillMetres, strata);
        long cells = (long)ground.Size * ground.Size;
        return new ErosionRun(result, new ErosionStats(seconds, stopwatch.Elapsed.TotalSeconds, 0, 0.0, cells * ErosionState.FloatsPerCell * sizeof(float))
        {
            MapsSeconds = stopwatch.Elapsed.TotalSeconds - seconds,
        });
    }
}
