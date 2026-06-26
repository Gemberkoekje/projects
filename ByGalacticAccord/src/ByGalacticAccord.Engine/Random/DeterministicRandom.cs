using System;
using System.Collections.Generic;

namespace ByGalacticAccord.Engine.Random;

/// <summary>
/// A seeded PRNG with a stable, self-implemented algorithm (xorshift128+ seeded via SplitMix64).
/// We do not use <see cref="System.Random"/> because its algorithm is not guaranteed stable across
/// runtimes, which would break replay (§5.5).
///
/// Journaling: the full draw sequence is re-derivable from <see cref="Seed"/> plus the call order,
/// and the call order is deterministic because every iteration that consumes randomness is over a
/// stably-ordered collection. <see cref="DrawCount"/> is exposed so a replay can be checked to have
/// consumed exactly the same number of draws (the §5.6 replay invariant).
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _s0;
    private ulong _s1;

    public DeterministicRandom(long seed)
    {
        Seed = seed;
        ulong z = unchecked((ulong)seed);
        _s0 = SplitMix64(ref z);
        _s1 = SplitMix64(ref z);
        if ((_s0 | _s1) == 0)
        {
            _s1 = 0x9E3779B97F4A7C15UL; // never all-zero state
        }
    }

    public long Seed { get; }

    public long DrawCount { get; private set; }

    /// <summary>Raw 64-bit draw (xorshift128+).</summary>
    public ulong NextUInt64()
    {
        DrawCount++;
        ulong s1 = _s0;
        ulong s0 = _s1;
        _s0 = s0;
        s1 ^= s1 << 23;
        _s1 = s1 ^ s0 ^ (s1 >> 18) ^ (s0 >> 5);
        return unchecked(_s1 + s0);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Uniform decimal in [0, 1), at double resolution. Decimal keeps economic math exact downstream.</summary>
    public decimal NextDecimal() => (decimal)NextDouble();

    /// <summary>Returns true with the given probability (clamped to [0, 1]).</summary>
    public bool Chance(decimal probability)
    {
        decimal p = Math.Clamp(probability, 0m, 1m);
        return NextDecimal() < p;
    }

    /// <summary>Inclusive integer in [minInclusive, maxInclusive].</summary>
    public long NextLong(long minInclusive, long maxInclusive)
    {
        if (maxInclusive <= minInclusive)
        {
            return minInclusive;
        }

        ulong span = (ulong)(maxInclusive - minInclusive) + 1UL;
        return minInclusive + (long)(NextUInt64() % span);
    }

    /// <summary>Uniform decimal in [min, max).</summary>
    public decimal NextDecimal(decimal min, decimal max) => min + (NextDecimal() * (max - min));

    public T Pick<T>(IReadOnlyList<T> items) => items[(int)NextLong(0, items.Count - 1)];

    private static ulong SplitMix64(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
