namespace Curator.Core.Game;

/// <summary>
/// The game's only source of randomness (BUILD_BRIEF §4.4): a stateless SplitMix64.
/// Every draw is derived from the game seed and the sequence number of the event being
/// produced, plus a salt when one command needs several draws, so a draw never depends
/// on how many other draws happened before it.
/// </summary>
public static class Prng
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    /// <summary>The SplitMix64 finaliser: a well-mixed 64-bit value for any input.</summary>
    /// <param name="value">The value to mix.</param>
    /// <returns>The mixed value.</returns>
    public static ulong Mix(ulong value)
    {
        var z = unchecked(value + GoldenGamma);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>A raw 64-bit draw for (seed, sequence, salt).</summary>
    /// <param name="seed">The game seed.</param>
    /// <param name="sequence">The sequence number of the event the draw is for.</param>
    /// <param name="salt">Distinguishes several draws made for the same event.</param>
    /// <returns>A uniformly distributed 64-bit value.</returns>
    public static ulong Draw(long seed, long sequence, int salt = 0)
    {
        var h = Mix(unchecked((ulong)seed));
        h = Mix(h ^ unchecked((ulong)sequence * GoldenGamma));
        return Mix(h ^ unchecked((ulong)salt));
    }

    /// <summary>A uniform integer in [0, <paramref name="maxExclusive"/>).</summary>
    /// <param name="seed">The game seed.</param>
    /// <param name="sequence">The sequence number of the event the draw is for.</param>
    /// <param name="maxExclusive">The exclusive upper bound; must be positive.</param>
    /// <param name="salt">Distinguishes several draws made for the same event.</param>
    /// <returns>An integer from 0 up to, but not including, <paramref name="maxExclusive"/>.</returns>
    public static int NextInt(long seed, long sequence, int maxExclusive, int salt = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        var high = Math.BigMul(Draw(seed, sequence, salt), (ulong)maxExclusive, out _);
        return (int)high;
    }

    /// <summary>A uniform double in [0, 1).</summary>
    /// <param name="seed">The game seed.</param>
    /// <param name="sequence">The sequence number of the event the draw is for.</param>
    /// <param name="salt">Distinguishes several draws made for the same event.</param>
    /// <returns>A double from 0 up to, but not including, 1.</returns>
    public static double NextDouble(long seed, long sequence, int salt = 0) =>
        (Draw(seed, sequence, salt) >> 11) * (1.0 / (1UL << 53));
}
