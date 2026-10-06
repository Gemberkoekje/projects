using System.Collections.Concurrent;

namespace SpaceTraders.Application.Exploring;

/// <summary>
/// The gates the API refused a jump to lately (PLAN.md slice 6.28): a way between systems leaves such a gate alone for
/// <see cref="ExploreAtlas.RecheckAfter"/>, as the explore plan does with the jumps it gives, which its own state keeps.
/// Every jump is recorded here, the probes' and the explore plan's alike, so no ship is sent through a gate that has just
/// refused one, and asked again on every pass.
/// </summary>
/// <remarks>In memory, a singleton: after a restart a gate that refused a jump may be tried once more.</remarks>
public sealed class JumpRefusals
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _refused = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that the API refused a jump to <paramref name="destinationGateWaypointSymbol"/>.</summary>
    /// <param name="destinationGateWaypointSymbol">The gate the ship was to jump to.</param>
    /// <param name="at">When the API refused it.</param>
    public void Record(string destinationGateWaypointSymbol, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationGateWaypointSymbol);
        _refused.AddOrUpdate(destinationGateWaypointSymbol, at, (_, earlier) => earlier > at ? earlier : at);
    }

    /// <summary>
    /// The explore plan's state with the refusals recorded here: each on the system of its gate, unless the state holds a later
    /// one. <see cref="ExploreAtlas.IsUsable"/> then leaves those gates alone for an hour.
    /// </summary>
    /// <param name="state">The explore plan's state.</param>
    /// <returns>The state, with the refusals.</returns>
    public ExplorePlanState Apply(ExplorePlanState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_refused.IsEmpty)
        {
            return state;
        }

        return state with
        {
            Systems =
            [
                .. state.Systems.Select(system => system.GateWaypointSymbol.Length > 0
                    && _refused.TryGetValue(system.GateWaypointSymbol, out var at)
                    && (system.JumpRefusedAt is null || system.JumpRefusedAt.Value < at)
                        ? system with { JumpRefusedAt = at }
                        : system),
            ],
        };
    }
}
