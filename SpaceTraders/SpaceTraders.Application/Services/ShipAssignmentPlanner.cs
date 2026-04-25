using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

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
/// Chooses between trading, scouting, and mining for a ship.
/// </summary>
public sealed class ShipAssignmentPlanner(
    ITradeOpportunityRepository tradeOpportunities,
    ISettingsRepository settings,
    IShipAssignmentRepository assignments,
    IWaypointRepository waypoints,
    IShipRepository ships) : IShipAssignmentPlanner
{
    private const string MineAssignmentType = "Mine";
    private const string ScoutAssignmentType = "Scout";
    private const string TradeAssignmentType = "Trade";
    private const string IdleAssignmentType = "Idle";

    public async Task<AssignShipCommand> PlanAsync(ShipModel ship, CancellationToken cancellationToken)
    {
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

        var targetMiningShips = (int)Math.Ceiling(miningCapableShips.Count * miningShipPercentage);
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
        var staleWaypoints = await waypoints.GetUnscoutedOrStaleAsync(ship.SystemSymbol, staleness, cancellationToken);
        var target = staleWaypoints.FirstOrDefault();

        return target is null
            ? null
            : new AssignShipCommand(ship.Symbol, ScoutAssignmentType, OriginWaypoint: target.Symbol);
    }

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
