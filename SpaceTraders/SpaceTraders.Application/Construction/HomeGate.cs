using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Construction;

/// <summary>
/// Whether the home system's jump gate is built (PLAN.md slice 6.63, D148): until then the bot trades in one system and is short
/// of cash, and a trade's actual fuel prices count; from then on the trade list's high-value trades do ("As soon as the gate is
/// built, we'll soon have 1000s of markets to trade with").
/// </summary>
/// <param name="sites">The home system.</param>
/// <param name="waypoints">The home system's gate, with whether it is under construction.</param>
/// <param name="constructions">The gate's construction site, as last fetched.</param>
public sealed class HomeGate(IConstructionSites sites, IWaypointRepository waypoints, IConstructionRepository constructions)
{
    /// <summary>
    /// Whether a jump gate of the home system is built: its construction site, as last fetched, is complete; or, where none is
    /// cached, the waypoint cache doesn't list it as under construction, as <see cref="ConstructionSites"/> reads it.
    /// </summary>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>True once it is built; false without a home system or a gate.</returns>
    public async Task<bool> IsBuiltAsync(CancellationToken cancellationToken)
    {
        var home = await sites.HomeSystemAsync(cancellationToken);
        if (home.Length == 0)
        {
            return false;
        }

        foreach (var gate in (await waypoints.GetBySystemAsync(home, cancellationToken))
            .Where(waypoint => waypoint.Type.Equals(ConstructionPlanner.JumpGateType, StringComparison.OrdinalIgnoreCase)))
        {
            var site = await constructions.FindAsync(gate.Symbol, cancellationToken);
            if (site is null ? !gate.IsUnderConstruction : site.IsComplete)
            {
                return true;
            }
        }

        return false;
    }
}
