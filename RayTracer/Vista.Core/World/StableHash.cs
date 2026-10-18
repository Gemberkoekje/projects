using System.Text;

namespace Vista.World;

/// <summary>
/// Stable 64-bit hashing for seed derivation, lattice noise and content fingerprints (vista-plan §4.1).
/// Everything a world depends on must hash identically across processes, machines and .NET versions, so
/// this never uses <see cref="string.GetHashCode()"/> (randomised per process) or <see cref="Random"/>
/// (its algorithm is not a compatibility contract). splitmix64 for mixing, FNV-1a for byte streams.
/// </summary>
public static class StableHash
{
    private const ulong FnvOffset = 14695981039346656037ul;
    private const ulong FnvPrime = 1099511628211ul;

    /// <summary>The splitmix64 finaliser: a bijective, well-avalanched mix of one 64-bit value.</summary>
    public static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15ul;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
        return z ^ (z >> 31);
    }

    /// <summary>Order-dependent combination of two hashes.</summary>
    public static ulong Combine(ulong a, ulong b) => Mix(a ^ Mix(b));

    /// <summary>FNV-1a over the UTF-8 bytes of <paramref name="text"/>.</summary>
    public static ulong Of(string text) => Fnv1a(FnvOffset, Encoding.UTF8.GetBytes(text));

    /// <summary>Hash of an integer lattice point under <paramref name="seed"/> (for gradient noise).</summary>
    public static ulong Lattice(ulong seed, long ix, long iy)
    {
        ulong h = Mix(seed + unchecked((ulong)ix * 0x9E3779B97F4A7C15ul));
        return Mix(h + unchecked((ulong)iy * 0xC2B2AE3D27D4EB4Ful));
    }

    /// <summary>Continues an FNV-1a hash <paramref name="hash"/> over <paramref name="bytes"/>.</summary>
    public static ulong Fnv1a(ulong hash, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return hash;
    }

    /// <summary>The FNV-1a starting value, for callers that hash a stream piecewise.</summary>
    public static ulong FnvStart => FnvOffset;
}

/// <summary>
/// Derives independent sub-seeds per world-generation stage from the user's seed (vista-plan §4.1). Keying
/// on the <i>stage</i> — not on the whole recipe — is what keeps the world still when an unrelated control
/// changes: switching dawn to dusk re-lights the scene but never reshuffles the terrain, and adding a new
/// stage later does not perturb the streams of the existing ones.
/// </summary>
public static class SeedDerivation
{
    /// <summary>Stage key for the base terrain shape.</summary>
    public const string TerrainBase = "terrain.base";

    /// <summary>The sub-seed for <paramref name="stage"/> under the user seed <paramref name="seed"/>.</summary>
    public static ulong Derive(ulong seed, string stage) => StableHash.Combine(seed, StableHash.Of(stage));
}
