using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Translates an abstract <see cref="ResourceProductionGoal"/> into a concrete <see cref="ShipGoal"/>
/// by resolving the best source waypoint (asteroid or gas giant) for the requested trade symbol.
/// Also exposes sell-market resolution so goal executors can delegate spatial queries here.
/// </summary>
/// <remarks>
/// Phase 9: the only layer that queries waypoint and market repositories for spatial decisions.
/// The orchestrator works with abstract resource needs; the executor handles all prerequisite navigation.
/// </remarks>
public sealed class AssignmentResolver(
    IWaypointRepository waypointRepository,
    IMarketRepository marketRepository) : IAssignmentResolver
{
    /// <summary>
    /// Trade symbols that originate from gas giants via siphoning rather than from asteroids via mining.
    /// </summary>
    private static readonly HashSet<string> GasSiphonableSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "HYDROCARBON",
        "LIQUID_HYDROGEN",
        "LIQUID_NITROGEN",
    };

    /// <inheritdoc/>
    public async Task<ShipGoal?> ResolveAsync(
        ShipModel ship,
        ResourceProductionGoal goal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(goal);

        var systemSymbol = ship.SystemSymbol;
        if (string.IsNullOrWhiteSpace(systemSymbol))
        {
            return null;
        }

        var systemWaypoints = await waypointRepository.GetBySystemAsync(systemSymbol, cancellationToken);
        if (systemWaypoints.Count == 0)
        {
            return null;
        }

        // Resolve the ship's current waypoint for distance-based ranking.
        WaypointCacheModel? shipWaypoint = null;
        if (!string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            shipWaypoint = systemWaypoints.FirstOrDefault(
                w => string.Equals(w.Symbol, ship.WaypointSymbol, StringComparison.OrdinalIgnoreCase));
        }

        if (goal.PurposeKind == ResourceProductionPurpose.Construction && !string.IsNullOrWhiteSpace(goal.DeliveryWaypoint))
        {
            return new SupplyConstructionGoal
            {
                TradeSymbol = goal.TradeSymbol,
                ConstructionSiteWaypointSymbol = goal.DeliveryWaypoint,
            };
        }

        return GasSiphonableSymbols.Contains(goal.TradeSymbol)
            ? ResolveGasSiphon(goal, systemWaypoints, shipWaypoint)
            : ResolveMining(goal, systemWaypoints, shipWaypoint);
    }

    /// <inheritdoc/>
    public async Task<string?> FindNearestSellMarketAsync(
        ShipModel ship,
        string tradeSymbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ship);
        if (string.IsNullOrWhiteSpace(tradeSymbol))
        {
            return null;
        }

        var systemSymbol = ship.SystemSymbol;
        if (string.IsNullOrWhiteSpace(systemSymbol))
        {
            return null;
        }

        var systemWaypoints = await waypointRepository.GetBySystemAsync(systemSymbol, cancellationToken);
        var marketSnapshots = await marketRepository.GetAllSnapshotsAsync(cancellationToken);

        if (systemWaypoints.Count == 0 || marketSnapshots.Count == 0)
        {
            return null;
        }

        // Resolve the ship's current waypoint for distance-based ranking.
        WaypointCacheModel? shipWaypoint = null;
        if (!string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            shipWaypoint = systemWaypoints.FirstOrDefault(
                w => string.Equals(w.Symbol, ship.WaypointSymbol, StringComparison.OrdinalIgnoreCase));
        }

        // Markets that import or exchange the trade symbol can buy it from the ship.
        var sellableMarkets = marketSnapshots
            .Where(m => string.Equals(m.SystemSymbol, systemSymbol, StringComparison.OrdinalIgnoreCase)
                && (m.Imports.Any(i => string.Equals(i, tradeSymbol, StringComparison.OrdinalIgnoreCase))
                    || m.Exchange.Any(e => string.Equals(e, tradeSymbol, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        if (sellableMarkets.Count == 0)
        {
            return null;
        }

        // Sort by coordinate distance from the ship's current waypoint.
        return sellableMarkets
            .OrderBy(m =>
            {
                var waypointData = systemWaypoints.FirstOrDefault(
                    w => string.Equals(w.Symbol, m.WaypointSymbol, StringComparison.OrdinalIgnoreCase));
                return waypointData is null ? double.MaxValue : DistanceFrom(waypointData, shipWaypoint);
            })
            .Select(m => m.WaypointSymbol)
            .First();
    }

    /// <inheritdoc/>
    public async Task<string?> FindBestSellMarketAsync(
        ShipModel ship,
        string tradeSymbol,
        string? preferredWaypoint,
        CancellationToken cancellationToken = default)
    {
        var nearest = await FindNearestSellMarketAsync(ship, tradeSymbol, cancellationToken);
        if (string.IsNullOrWhiteSpace(preferredWaypoint))
        {
            return nearest;
        }

        var systemSymbol = ship.SystemSymbol;
        if (string.IsNullOrWhiteSpace(systemSymbol))
        {
            return nearest;
        }

        var marketSnapshots = await marketRepository.GetAllSnapshotsAsync(cancellationToken);
        var preferredExists = marketSnapshots.Any(m =>
            m.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase)
            && m.WaypointSymbol.Equals(preferredWaypoint, StringComparison.OrdinalIgnoreCase)
            && (m.Imports.Any(i => i.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase))
                || m.Exchange.Any(e => e.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase))));

        return preferredExists ? preferredWaypoint : nearest;
    }

    /// <inheritdoc/>
    public async Task<ShipGoal> ResolveMarketCoverageAsync(
        string waypointSymbol,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(waypointSymbol);

        var snapshot = await marketRepository.FindSnapshotByWaypointAsync(waypointSymbol, cancellationToken);
        if (snapshot is null)
        {
            return new ScoutWaypointGoal { TargetWaypointSymbol = waypointSymbol };
        }

        return new PatrolMarketGoal { TargetWaypointSymbol = waypointSymbol };
    }

    private static ShipGoal? ResolveGasSiphon(
        ResourceProductionGoal goal,
        IReadOnlyList<WaypointCacheModel> systemWaypoints,
        WaypointCacheModel? shipWaypoint)
    {
        var gasGiant = systemWaypoints
            .Where(w => w.Type.Contains("GAS_GIANT", StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => DistanceFrom(w, shipWaypoint))
            .FirstOrDefault();

        if (gasGiant is null)
        {
            return null;
        }

        return new SiphonResourceGoal
        {
            TradeSymbol = goal.TradeSymbol,
            SourceWaypointSymbol = gasGiant.Symbol,
        };
    }

    private static ShipGoal? ResolveMining(
        ResourceProductionGoal goal,
        IReadOnlyList<WaypointCacheModel> systemWaypoints,
        WaypointCacheModel? shipWaypoint)
    {
        var asteroids = systemWaypoints
            .Where(w => IsAsteroid(w.Type))
            .ToList();

        if (asteroids.Count == 0)
        {
            return null;
        }

        // Prefer asteroids whose known traits/modifiers mention the requested trade symbol;
        // when no trait match exists, fall back to the nearest asteroid (deposit may still be present).
        var bestAsteroid = asteroids
            .OrderByDescending(w => ScoreTradeSymbolMatch(w, goal.TradeSymbol))
            .ThenBy(w => DistanceFrom(w, shipWaypoint))
            .First();

        return new MineResourceGoal
        {
            TradeSymbol = goal.TradeSymbol,
            SourceWaypointSymbol = bestAsteroid.Symbol,
        };
    }

    private static bool IsAsteroid(string waypointType) =>
        waypointType.Contains("ASTEROID", StringComparison.OrdinalIgnoreCase);

    private static int ScoreTradeSymbolMatch(WaypointCacheModel waypoint, string tradeSymbol)
    {
        var haystack = $"{waypoint.TraitsJson} {waypoint.ModifiersJson}";
        var score = 0;

        if (haystack.Contains(tradeSymbol, StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        var normalizedSymbol = tradeSymbol.Replace('_', ' ');
        if (haystack.Contains(normalizedSymbol, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (tradeSymbol.EndsWith("_ORE", StringComparison.OrdinalIgnoreCase)
            && haystack.Contains("ORE", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (tradeSymbol.EndsWith("_ICE", StringComparison.OrdinalIgnoreCase)
            && haystack.Contains("ICE", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static double DistanceFrom(WaypointCacheModel waypoint, WaypointCacheModel? origin)
    {
        if (origin is null)
        {
            return Math.Sqrt(((double)waypoint.X * waypoint.X) + ((double)waypoint.Y * waypoint.Y));
        }

        var dx = waypoint.X - origin.X;
        var dy = waypoint.Y - origin.Y;
        return Math.Sqrt(((double)dx * dx) + ((double)dy * dy));
    }
}
