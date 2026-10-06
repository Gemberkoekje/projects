using System.Collections.Concurrent;

namespace SpaceTraders.Application.Exploring;

/// <summary>
/// The systems the API refused a warp into lately (PLAN.md slice 6.31): a way between systems warps into none of them for
/// <see cref="ExploreAtlas.RecheckAfter"/>, as it leaves a gate that refused a jump alone (<see cref="JumpRefusals"/>). Sent
/// again, the warp would be refused again on every step.
/// </summary>
/// <remarks>In memory, a singleton: after a restart a system that refused a warp may be tried once more.</remarks>
public sealed class WarpRefusals
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _refused = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that the API refused a warp into <paramref name="systemSymbol"/>.</summary>
    /// <param name="systemSymbol">The system the ship was to warp to.</param>
    /// <param name="at">When the API refused it.</param>
    public void Record(string systemSymbol, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemSymbol);
        _refused.AddOrUpdate(systemSymbol, at, (_, earlier) => earlier > at ? earlier : at);
    }

    /// <summary>The systems the API refused a warp into within <see cref="ExploreAtlas.RecheckAfter"/> of <paramref name="now"/>.</summary>
    /// <param name="now">The time to judge by.</param>
    /// <returns>The systems.</returns>
    public IReadOnlySet<string> Refused(DateTimeOffset now)
        => _refused
            .Where(entry => now - entry.Value < ExploreAtlas.RecheckAfter)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
