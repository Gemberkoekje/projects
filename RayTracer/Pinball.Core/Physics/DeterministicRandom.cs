namespace Pinball.Physics;

/// <summary>
/// A small, seeded, deterministic PRNG (splitmix64) threaded through the simulation so any stochastic term
/// (VPX-style bounce scatter, §6.1.9) is reproducible: a given (seed, input timeline) replays bit-for-bit
/// within a build (§6.1.5). Not cryptographic; chosen for a stable, portable, well-distributed sequence.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    /// <summary>Creates a generator seeded with <paramref name="seed"/>.</summary>
    public DeterministicRandom(ulong seed) => _state = seed;

    /// <summary>Next raw 64-bit value (splitmix64).</summary>
    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15ul;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
        return z ^ (z >> 31);
    }

    /// <summary>Next double in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Next double in [−1, 1).</summary>
    public double NextSigned() => NextDouble() * 2.0 - 1.0;
}
