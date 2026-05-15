using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Automation;

public interface IScoutMarketplaceDiscoveryService
{
    Task<IReadOnlyList<WaypointCacheModel>> DiscoverAsync(ShipModel scoutShip, CancellationToken cancellationToken = default);
}

public sealed class ScoutMarketplaceDiscoveryService(
    IWaypointRepository waypoints,
    ILogger<ScoutMarketplaceDiscoveryService> logger) : IScoutMarketplaceDiscoveryService
{
    public async Task<IReadOnlyList<WaypointCacheModel>> DiscoverAsync(
        ShipModel scoutShip,
        CancellationToken cancellationToken = default)
    {
        var startingSystem = ResolveStartingSystemSymbol(scoutShip);

        var knownWaypoints = await waypoints.GetBySystemAsync(startingSystem, cancellationToken);
        var marketplaces = knownWaypoints
            .Where(waypoint => waypoint.HasMarket)
            .OrderBy(waypoint => waypoint.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        logger.LogDebug(
            "Scout marketplace discovery for ship {ShipSymbol} in system {SystemSymbol}: {Count} market waypoints found.",
            scoutShip.Symbol,
            startingSystem,
            marketplaces.Count);

        return marketplaces;
    }

    private static string ResolveStartingSystemSymbol(ShipModel scoutShip)
    {
        if (!string.IsNullOrWhiteSpace(scoutShip.SystemSymbol))
        {
            return scoutShip.SystemSymbol;
        }

        if (!string.IsNullOrWhiteSpace(scoutShip.WaypointSymbol))
        {
            var lastDash = scoutShip.WaypointSymbol.LastIndexOf('-');
            if (lastDash > 0)
            {
                return scoutShip.WaypointSymbol[..lastDash];
            }

            return scoutShip.WaypointSymbol;
        }

        throw new InvalidOperationException(
            $"Scout marketplace discovery failed for ship {scoutShip.Symbol}: no starting system symbol is available.");
    }
}
