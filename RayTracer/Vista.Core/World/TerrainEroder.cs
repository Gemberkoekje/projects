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

    /// <summary>Erodes <paramref name="ground"/> with <paramref name="settings"/>.</summary>
    ErosionRun Erode(HeightField ground, ErosionSettings settings);
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
}

/// <summary>The C# reference as an <see cref="ITerrainEroder"/>: exact, but some 10⁴ times slower than the GPU, so for small grids.</summary>
public sealed class ReferenceEroder : ITerrainEroder
{
    /// <inheritdoc />
    public string DeviceName => "C# reference";

    /// <inheritdoc />
    public string DriverVersion => string.Empty;

    /// <inheritdoc />
    public ErosionRun Erode(HeightField ground, ErosionSettings settings)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        ErosionResult result = ErosionReference.Erode(ground, settings);
        double seconds = stopwatch.Elapsed.TotalSeconds;
        long cells = (long)ground.Size * ground.Size;
        return new ErosionRun(result, new ErosionStats(seconds, seconds, 0, 0.0, cells * ErosionState.FloatsPerCell * sizeof(float)));
    }
}
