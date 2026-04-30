using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Orchestration;

/// <summary>
/// Estimates aggregate fleet capacity for orchestrator decisions:
/// how many ships can mine, haul, trade, or scout, and how much
/// total/idle cargo capacity is available.
/// </summary>
public interface IFleetCapacityEstimator
{
    Task<FleetCapacityEstimate> EstimateAsync(CancellationToken cancellationToken = default);
}

public sealed class FleetCapacityEstimator(
    IShipRepository ships,
    IShipAssignmentRepository assignments) : IFleetCapacityEstimator
{
    public async Task<FleetCapacityEstimate> EstimateAsync(CancellationToken cancellationToken = default)
    {
        var fleet = await ships.GetAllAsync(cancellationToken);
        var activeAssignments = await assignments.GetAllActiveAsync(cancellationToken);
        var assignmentByShip = activeAssignments
            .Where(a => !a.CompletedAt.HasValue)
            .GroupBy(a => a.ShipSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().AssignmentType, StringComparer.OrdinalIgnoreCase);

        var miningShips = 0;
        var haulingShips = 0;
        var tradingShips = 0;
        var scoutingShips = 0;
        var idleShips = 0;
        var totalCargoCapacity = 0;
        var idleCargoCapacity = 0;

        foreach (var ship in fleet)
        {
            totalCargoCapacity += ship.CargoCapacity;

            var assignmentType = assignmentByShip.TryGetValue(ship.Symbol, out var t) ? t : null;
            if (string.IsNullOrWhiteSpace(assignmentType) ||
                assignmentType.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            {
                idleShips++;
                idleCargoCapacity += ship.CargoCapacity;
            }
            else if (assignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase) ||
                     assignmentType.Equals("Siphon", StringComparison.OrdinalIgnoreCase))
            {
                miningShips++;
            }
            else if (assignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
            {
                tradingShips++;
            }
            else if (assignmentType.Equals("Contract", StringComparison.OrdinalIgnoreCase) ||
                     assignmentType.Equals("Builder", StringComparison.OrdinalIgnoreCase))
            {
                haulingShips++;
            }
            else if (assignmentType.Equals("Scout", StringComparison.OrdinalIgnoreCase) ||
                     assignmentType.Equals("MarketProbe", StringComparison.OrdinalIgnoreCase))
            {
                scoutingShips++;
            }
        }

        return new FleetCapacityEstimate(
            TotalShips: fleet.Count,
            MiningShips: miningShips,
            HaulingShips: haulingShips,
            TradingShips: tradingShips,
            ScoutingShips: scoutingShips,
            IdleShips: idleShips,
            TotalCargoCapacity: totalCargoCapacity,
            IdleCargoCapacity: idleCargoCapacity);
    }
}
