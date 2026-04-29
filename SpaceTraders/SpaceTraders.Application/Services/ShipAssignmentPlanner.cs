using System.Text.Json;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

public interface IContractObjectivePlanner
{
    Task<AssignShipCommand> PlanAsync(ShipModel ship, CancellationToken cancellationToken);
}

public sealed class ContractObjectivePlanner(
    IContractRepository contracts,
    IMarketRepository markets) : IContractObjectivePlanner
{
    public async Task<AssignShipCommand> PlanAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        var activeContracts = await contracts.GetActiveAsync(cancellationToken);
        var activeContract = activeContracts
            .Where(c => c.IsAccepted && !c.IsFulfilled)
            .OrderBy(c => c.TermsDeadline ?? c.Expiration ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();

        if (activeContract is null)
        {
            return null!;
        }

        var deliverable = DeserializeDeliverables(activeContract.DeliverablesJson)
            .FirstOrDefault(d => d.UnitsRequired > d.UnitsFulfilled);

        if (deliverable is null)
        {
            return null!;
        }

        var remainingUnits = deliverable.UnitsRequired - deliverable.UnitsFulfilled;
        var originWaypoint = await ResolveOriginWaypointAsync(ship, deliverable.TradeSymbol, cancellationToken);

        return new AssignShipCommand(
            ship.Symbol,
            "Contract",
            OriginWaypoint: originWaypoint,
            DestWaypoint: deliverable.DestinationSymbol,
            CargoSymbol: deliverable.TradeSymbol,
            ContractId: activeContract.Id,
            RequiredUnits: remainingUnits);
    }

    private async Task<string> ResolveOriginWaypointAsync(ShipModel ship, string tradeSymbol, CancellationToken cancellationToken)
    {
        var cargoUnits = ship.CargoInventory?
            .FirstOrDefault(i => i.Symbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        if (cargoUnits > 0 && !string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            return ship.WaypointSymbol;
        }

        var marketSnapshots = await markets.GetAllSnapshotsAsync(cancellationToken);
        var sameSystemMatch = marketSnapshots.FirstOrDefault(snapshot =>
            string.Equals(snapshot.SystemSymbol, ship.SystemSymbol, StringComparison.OrdinalIgnoreCase) &&
            SupportsTradeSymbol(snapshot, tradeSymbol));

        if (sameSystemMatch is not null)
        {
            return sameSystemMatch.WaypointSymbol;
        }

        var anyMatch = marketSnapshots.FirstOrDefault(snapshot => SupportsTradeSymbol(snapshot, tradeSymbol));
        return anyMatch?.WaypointSymbol ?? ship.WaypointSymbol ?? string.Empty;
    }

    private static bool SupportsTradeSymbol(MarketSnapshot snapshot, string tradeSymbol)
        => snapshot.Exports.Any(symbol => symbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase))
           || snapshot.Exchange.Any(symbol => symbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase))
           || snapshot.Imports.Any(symbol => symbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ContractDeliverableDto> DeserializeDeliverables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ContractDeliverableDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// Plans the next assignment for a ship based on market opportunities and fleet balance.
/// </summary>
public interface IShipAssignmentPlanner
{
    /// <summary>
    /// Builds the next assignment for the specified ship.
    /// </summary>
    Task<AssignShipCommand> PlanAsync(ShipModel ship, CancellationToken cancellationToken);
}

/// <summary>
/// Chooses between contract work, trading, scouting, and mining for a ship.
/// </summary>
public sealed class ShipAssignmentPlanner(
    IContractObjectivePlanner contractObjectives,
    ITradeOpportunityRepository tradeOpportunities,
    ISettingsRepository settings,
    IShipAssignmentRepository assignments,
    IWaypointRepository waypoints,
    IShipRepository ships,
    IMarketRepository markets) : IShipAssignmentPlanner
{
    private sealed class NoopContractObjectivePlanner : IContractObjectivePlanner
    {
        public Task<AssignShipCommand> PlanAsync(ShipModel ship, CancellationToken cancellationToken)
            => Task.FromResult<AssignShipCommand>(null!);
    }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipAssignmentPlanner(
        ITradeOpportunityRepository tradeOpportunities,
        ISettingsRepository settings,
        IShipAssignmentRepository assignments,
        IWaypointRepository waypoints,
        IShipRepository ships)
        : this(new NoopContractObjectivePlanner(), tradeOpportunities, settings, assignments, waypoints, ships, new NoopMarketRepository())
    {
    }

    private sealed class NoopMarketRepository : IMarketRepository
    {
        public Task<DateTimeOffset?> GetLastObservedAtAsync(string waypointSymbol, CancellationToken cancellationToken = default)
            => Task.FromResult<DateTimeOffset?>(null);

        public Task UpsertAsync(MarketDataModel market, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<MarketSnapshot?> FindSnapshotByWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default)
            => Task.FromResult<MarketSnapshot?>(null);

        public Task<IReadOnlyList<MarketSnapshot>> GetAllSnapshotsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MarketSnapshot>>([]);
    }

    private const string MineAssignmentType = "Mine";
    private const string ScoutAssignmentType = "Scout";
    private const string TradeAssignmentType = "Trade";
    private const string SiphonAssignmentType = "Siphon";
    private const string IdleAssignmentType = "Idle";

    public async Task<AssignShipCommand> PlanAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        var contractAssignment = await contractObjectives.PlanAsync(ship, cancellationToken);
        if (contractAssignment is not null)
        {
            return contractAssignment;
        }

        var minProfit = await settings.GetAsync<int>("Trade.MinProfitPerUnit", cancellationToken);
        var maxDistance = await settings.GetAsync<int>("Trade.MaxHaulDistance", cancellationToken);
        var miningShipPercentage = await settings.GetAsync<decimal>("Automation.MiningShipPercentage", cancellationToken);
        var effectiveMiningShipPercentage = decimal.Clamp(
            miningShipPercentage == 0m ? 0.25m : miningShipPercentage,
            0m,
            1m);

        var route = await tradeOpportunities.GetBestRouteForCapacityAsync(
            cargoCapacity: ship.CargoCapacity > 0 ? ship.CargoCapacity : int.MaxValue,
            minProfitPerUnit: minProfit,
            maxDistanceJumps: maxDistance,
            cancellationToken: cancellationToken);

        if (ship.HasMiningEquipment && await ShouldAssignMiningAsync(ship.Symbol, effectiveMiningShipPercentage, cancellationToken))
        {
            var mineAssignment = await BuildMiningAssignmentAsync(ship, cancellationToken);
            if (mineAssignment is not null)
            {
                return mineAssignment;
            }
        }

        if (ship.HasGasSiphonEquipment && ship.HasGasProcessor)
        {
            var siphonAssignment = await BuildSiphonAssignmentAsync(ship, cancellationToken);
            if (siphonAssignment is not null)
            {
                return siphonAssignment;
            }
        }

        if (route is not null)
        {
            return new AssignShipCommand(
                ship.Symbol,
                TradeAssignmentType,
                OriginWaypoint: route.BuyWaypoint,
                DestWaypoint: route.SellWaypoint,
                CargoSymbol: route.TradeSymbol);
        }

        var scoutAssignment = await BuildScoutAssignmentAsync(ship, cancellationToken);
        if (scoutAssignment is not null)
        {
            return scoutAssignment;
        }

        return new AssignShipCommand(ship.Symbol, IdleAssignmentType);
    }

    private async Task<AssignShipCommand> BuildSiphonAssignmentAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ship.SystemSymbol))
        {
            return null!;
        }

        var systemWaypoints = await waypoints.GetBySystemAsync(ship.SystemSymbol, cancellationToken);
        var gasGiant = systemWaypoints.FirstOrDefault(w => w.Type.Contains("GAS_GIANT", StringComparison.OrdinalIgnoreCase));
        var market = systemWaypoints.FirstOrDefault(w => w.HasMarket);
        if (gasGiant is null || market is null)
        {
            return null!;
        }

        return new AssignShipCommand(
            ship.Symbol,
            SiphonAssignmentType,
            OriginWaypoint: gasGiant.Symbol,
            DestWaypoint: market.Symbol,
            CargoSymbol: "HYDROCARBON");
    }

    private async Task<AssignShipCommand> BuildMiningAssignmentAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ship.SystemSymbol))
        {
            return null!;
        }

        var systemWaypoints = await waypoints.GetBySystemAsync(ship.SystemSymbol, cancellationToken);
        var marketSnapshots = await markets.GetAllSnapshotsAsync(cancellationToken);
        var targetCargo = await ResolvePreferredResourceAsync(ship, cancellationToken);

        var asteroid = systemWaypoints
            .Where(w => w.Type.Contains("ASTEROID", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(w => ScoreResourceTargetMatch(w, targetCargo))
            .ThenBy(w => GetAsteroidPriority(w))
            .ThenBy(w => w.LastObservedAt)
            .FirstOrDefault();

        var market = SelectBestSellWaypoint(systemWaypoints, marketSnapshots, targetCargo);

        if (asteroid is null || market is null)
        {
            return null!;
        }

        return new AssignShipCommand(
            ship.Symbol,
            MineAssignmentType,
            OriginWaypoint: asteroid.Symbol,
            DestWaypoint: market.Symbol,
            CargoSymbol: targetCargo);
    }

    private async Task<string> ResolvePreferredResourceAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        var contractAssignment = await contractObjectives.PlanAsync(ship, cancellationToken);
        if (contractAssignment is not null && !string.IsNullOrWhiteSpace(contractAssignment.CargoSymbol))
        {
            return contractAssignment.CargoSymbol;
        }

        var routes = await tradeOpportunities.GetTopRoutesAsync(10, cancellationToken);
        return routes.FirstOrDefault(r =>
                r.TradeSymbol.EndsWith("_ORE", StringComparison.OrdinalIgnoreCase) ||
                r.TradeSymbol.EndsWith("_ICE", StringComparison.OrdinalIgnoreCase))?.TradeSymbol
            ?? "IRON_ORE";
    }

    private static int ScoreResourceTargetMatch(WaypointCacheModel waypoint, string targetCargo)
    {
        if (string.IsNullOrWhiteSpace(targetCargo))
        {
            return 0;
        }

        var score = 0;
        var haystack = $"{waypoint.TraitsJson} {waypoint.ModifiersJson}";
        var normalizedTarget = targetCargo.Replace('_', ' ');

        if (haystack.Contains(targetCargo, StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }
        if (haystack.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }
        if (targetCargo.EndsWith("_ORE", StringComparison.OrdinalIgnoreCase) && haystack.Contains("ORE", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }
        if (targetCargo.EndsWith("_ICE", StringComparison.OrdinalIgnoreCase) && haystack.Contains("ICE", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }
        if (!string.IsNullOrWhiteSpace(waypoint.ModifiersJson) && waypoint.ModifiersJson.Contains("COMMON", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        return score;
    }

    private static int GetAsteroidPriority(WaypointCacheModel waypoint)
    {
        var score = 0;
        var modifiers = waypoint.ModifiersJson ?? string.Empty;
        var traits = waypoint.TraitsJson ?? string.Empty;

        if (modifiers.Contains("UNSTABLE", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }
        if (modifiers.Contains("VOLATILE", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }
        if (modifiers.Contains("STRIPPED", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }
        if (modifiers.Contains("WEAK_GRAVITY", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }
        if (traits.Contains("ENGINEERED", StringComparison.OrdinalIgnoreCase))
        {
            score -= 20;
        }
        if (traits.Contains("PRECIOUS", StringComparison.OrdinalIgnoreCase) || traits.Contains("RARE", StringComparison.OrdinalIgnoreCase))
        {
            score -= 10;
        }
        if (traits.Contains("COMMON", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        return score;
    }

    private static WaypointCacheModel SelectBestSellWaypoint(
        IReadOnlyList<WaypointCacheModel> systemWaypoints,
        IReadOnlyList<MarketSnapshot> marketSnapshots,
        string targetCargo)
    {
        var marketByWaypoint = marketSnapshots.ToDictionary(m => m.WaypointSymbol, StringComparer.OrdinalIgnoreCase);
        return systemWaypoints
            .Where(w => w.HasMarket)
            .OrderByDescending(w => marketByWaypoint.TryGetValue(w.Symbol, out var market)
                ? market.TradeGoods.FirstOrDefault(g => g.Symbol.Equals(targetCargo, StringComparison.OrdinalIgnoreCase))?.SellPrice ?? 0
                : 0)
            .ThenBy(w => w.LastObservedAt)
            .FirstOrDefault()!;
    }

    private async Task<bool> ShouldAssignMiningAsync(string shipSymbol, decimal miningShipPercentage, CancellationToken cancellationToken)
    {
        var fleet = await ships.GetAllAsync(cancellationToken);
        var miningCapableShips = fleet
            .Where(s => s.HasMiningEquipment)
            .Select(s => s.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!miningCapableShips.Contains(shipSymbol) || miningCapableShips.Count == 0)
        {
            return false;
        }

        var exactTarget = miningCapableShips.Count * miningShipPercentage;
        var targetMiningShips = (int)exactTarget;
        if (targetMiningShips < exactTarget)
        {
            targetMiningShips++;
        }
        if (targetMiningShips <= 0)
        {
            return false;
        }

        var activeAssignments = await assignments.GetAllActiveAsync(cancellationToken);
        var activeMiningAssignments = activeAssignments.Count(a =>
            miningCapableShips.Contains(a.ShipSymbol) &&
            a.AssignmentType.Equals(MineAssignmentType, StringComparison.OrdinalIgnoreCase));

        return activeMiningAssignments < targetMiningShips;
    }

    private async Task<AssignShipCommand?> BuildScoutAssignmentAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ship.SystemSymbol))
        {
            return null;
        }

        var refreshIntervalMinutes = await settings.GetAsync<int>("Scout.MarketRefreshIntervalMinutes", cancellationToken);
        var staleness = TimeSpan.FromMinutes(refreshIntervalMinutes > 0 ? refreshIntervalMinutes : 10);

        var explorationAssignment = await BuildExplorationAssignmentAsync(ship, cancellationToken);
        if (explorationAssignment is not null)
        {
            return explorationAssignment;
        }

        if (IsProbeScout(ship))
        {
            var probeTarget = await BuildProbeMarketScoutAssignmentAsync(ship, staleness, cancellationToken);
            if (probeTarget is not null)
            {
                return probeTarget;
            }
        }

        var staleWaypoints = await waypoints.GetUnscoutedOrStaleAsync(ship.SystemSymbol, staleness, cancellationToken);
        var target = staleWaypoints.FirstOrDefault();

        return target is null
            ? null
            : new AssignShipCommand(ship.Symbol, ScoutAssignmentType, OriginWaypoint: target.Symbol);
    }

    private async Task<AssignShipCommand?> BuildExplorationAssignmentAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        var systemWaypoints = await waypoints.GetBySystemAsync(ship.SystemSymbol ?? string.Empty, cancellationToken);
        if (systemWaypoints.Count == 0)
        {
            return null;
        }

        var unchartedTarget = systemWaypoints
            .Where(w => string.IsNullOrWhiteSpace(w.ChartJson))
            .OrderBy(w => w.HasShipyard ? 0 : 1)
            .ThenBy(w => w.HasMarket ? 0 : 1)
            .ThenBy(w => IsHighValueAsteroid(w) ? 0 : 1)
            .FirstOrDefault();

        if (unchartedTarget is not null)
        {
            return new AssignShipCommand(ship.Symbol, ScoutAssignmentType, OriginWaypoint: unchartedTarget.Symbol);
        }

        var asteroidTarget = systemWaypoints
            .Where(IsHighValueAsteroid)
            .OrderBy(w => w.LastObservedAt)
            .FirstOrDefault();

        return asteroidTarget is null
            ? null
            : new AssignShipCommand(ship.Symbol, ScoutAssignmentType, OriginWaypoint: asteroidTarget.Symbol);
    }

    private static bool IsHighValueAsteroid(WaypointCacheModel waypoint)
    {
        if (!waypoint.Type.Contains("ASTEROID", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var traits = waypoint.TraitsJson ?? string.Empty;
        return traits.Contains("ENGINEERED", StringComparison.OrdinalIgnoreCase)
            || traits.Contains("PRECIOUS", StringComparison.OrdinalIgnoreCase)
            || traits.Contains("RARE", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<AssignShipCommand?> BuildProbeMarketScoutAssignmentAsync(
        ShipModel ship,
        TimeSpan staleness,
        CancellationToken cancellationToken)
    {
        var systemWaypoints = await waypoints.GetBySystemAsync(ship.SystemSymbol ?? string.Empty, cancellationToken);
        if (systemWaypoints.Count == 0)
        {
            return null;
        }

        var now = TimeProvider.System.GetUtcNow();
        var staleThreshold = now - staleness;

        var importantTarget = systemWaypoints
            .Where(w => w.HasMarket || w.HasShipyard)
            .OrderBy(w => w.LastObservedAt >= staleThreshold)
            .ThenBy(w => w.HasMarket ? 0 : 1)
            .ThenBy(w => w.LastObservedAt)
            .FirstOrDefault();

        if (importantTarget is null)
        {
            return null;
        }

        return new AssignShipCommand(ship.Symbol, ScoutAssignmentType, OriginWaypoint: importantTarget.Symbol);
    }

    private static bool IsProbeScout(ShipModel ship)
        => ship.ShipType.Equals("SHIP_PROBE", StringComparison.OrdinalIgnoreCase)
           || ship.ShipType.Equals("SHIP_LIGHT_HAULER", StringComparison.OrdinalIgnoreCase)
           || ship.Symbol.Contains("PROBE", StringComparison.OrdinalIgnoreCase);
}
