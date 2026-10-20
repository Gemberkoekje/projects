using Vista.World;

namespace Vista.Tests;

/// <summary>
/// Worlds small enough for CI. The real grid (<see cref="WorldGrid.Default"/>, 4097² since M1.7) takes a second to generate and
/// most of a gigabyte to mesh; the pipeline tests run on this one, which spans the same 32.8 km in 65 samples.
/// </summary>
internal static class TestWorlds
{
    /// <summary>65² samples 512 m apart.</summary>
    public static readonly WorldGrid Grid = new(65, 512.0);
}
