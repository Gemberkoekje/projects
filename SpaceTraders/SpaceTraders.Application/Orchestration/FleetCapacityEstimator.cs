using SpaceTraders.Application.Interfaces;
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

/// <summary>
/// Phase 12c: role classification is now derived from <see cref="IShipCapabilityRegistry"/>
/// rather than from the assignment-type string.  Assignment data is still consulted to
/// distinguish idle ships (no active assignment) from ships that have an active role.
/// </summary>
public sealed class FleetCapacityEstimator(
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    IShipCapabilityRegistry registry) : IFleetCapacityEstimator
{
    public async Task<FleetCapacityEstimate> EstimateAsync(CancellationToken cancellationToken = default)
    {
        var fleet = await ships.GetAllAsync(cancellationToken);
        var activeAssignments = await assignments.GetAllActiveAsync(cancellationToken);
        var activeShipSymbols = activeAssignments
            .Where(a => !a.CompletedAt.HasValue)
            .Select(a => a.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var miningShips = 0;
        var haulingShips = 0;
        var scoutingShips = 0;
        var idleShips = 0;
        var totalCargoCapacity = 0;
        var idleCargoCapacity = 0;

        foreach (var ship in fleet)
        {
            totalCargoCapacity += ship.CargoCapacity;

            if (!activeShipSymbols.Contains(ship.Symbol))
            {
                idleShips++;
                idleCargoCapacity += ship.CargoCapacity;
                continue;
            }

            var caps = registry.GetCapabilities(ship);
            if (caps.CanMine || caps.CanSiphon)
            {
                miningShips++;
            }
            else if (!caps.HasCargo)
            {
                scoutingShips++;
            }
            else
            {
                haulingShips++;
            }
        }

        return new FleetCapacityEstimate(
            TotalShips: fleet.Count,
            MiningShips: miningShips,
            HaulingShips: haulingShips,
            // TradingShips cannot be distinguished from HaulingShips via capabilities alone;
            // the field is retained in the record for compatibility but is always 0 here.
            TradingShips: 0,
            ScoutingShips: scoutingShips,
            IdleShips: idleShips,
            TotalCargoCapacity: totalCargoCapacity,
            IdleCargoCapacity: idleCargoCapacity);
    }
}
