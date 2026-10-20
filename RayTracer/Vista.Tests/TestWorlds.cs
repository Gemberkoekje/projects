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

    /// <summary>65² samples 64 m apart (4.1 km): the view-dependent mesh's tests (M1.9), whose detail below the grid starts at 128 m.</summary>
    public static readonly WorldGrid Small = new(65, 64.0);
}
