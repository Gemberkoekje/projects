using System.Text.Json;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

public interface IContractObjectivePlanner
{
    Task<AssignShipCommand?> PlanAsync(ShipModel ship, CancellationToken cancellationToken);
}

public sealed class ContractObjectivePlanner(
    IContractRepository contracts,
    IMarketRepository markets) : IContractObjectivePlanner
{
    public async Task<AssignShipCommand?> PlanAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        var activeContracts = await contracts.GetActiveAsync(cancellationToken);
        var activeContract = activeContracts
            .Where(c => c.IsAccepted && !c.IsFulfilled)
            .OrderBy(c => c.TermsDeadline ?? c.Expiration ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();

        if (activeContract is null)
        {
            return null;
        }

        var deliverable = DeserializeDeliverables(activeContract.DeliverablesJson)
            .FirstOrDefault(d => d.UnitsRequired > d.UnitsFulfilled);

        if (deliverable is null)
        {
            return null;
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
    IShipRepository ships) : IShipAssignmentPlanner
{
    private sealed class NoopContractObjectivePlanner : IContractObjectivePlanner
    {
        public Task<AssignShipCommand?> PlanAsync(ShipModel ship, CancellationToken cancellationToken)
            => Task.FromResult<AssignShipCommand?>(null);
    }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipAssignmentPlanner(
        ITradeOpportunityRepository tradeOpportunities,
        ISettingsRepository settings,
        IShipAssignmentRepository assignments,
        IWaypointRepository waypoints,
        IShipRepository ships)
        : this(new NoopContractObjectivePlanner(), tradeOpportunities, settings, assignments, waypoints, ships)
    {
    }

    private const string MineAssignmentType = "Mine";
    private const string ScoutAssignmentType = "Scout";
    private const string TradeAssignmentType = "Trade";
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
    
    private async Task<AssignShipCommand?> BuildMiningAssignmentAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ship.SystemSymbol))
        {
            return null;
        }

        var systemWaypoints = await waypoints.GetBySystemAsync(ship.SystemSymbol, cancellationToken);
        var asteroid = systemWaypoints.FirstOrDefault(w => w.Type.Contains("ASTEROID", StringComparison.OrdinalIgnoreCase));
        var market = systemWaypoints.FirstOrDefault(w => w.HasMarket);

        if (asteroid is null || market is null)
        {
            return null;
        }

        return new AssignShipCommand(
            ship.Symbol,
            MineAssignmentType,
            OriginWaypoint: asteroid.Symbol,
            DestWaypoint: market.Symbol);
    }
}
