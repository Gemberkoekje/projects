using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Orchestration;

/// <summary>
/// High-level fleet orchestrator. Aggregates goals from all goal evaluators,
/// matches idle ships to the highest-priority goals, and emits one
/// <see cref="AssignShipCommand"/> per ship. The orchestrator never issues
/// low-level ship commands (Dock/Orbit/Navigate); ship planners do that.
/// </summary>
public interface IFleetOrchestrator
{
    Task EvaluateAndAssignAsync(CancellationToken cancellationToken = default);
}

public sealed class FleetOrchestrator(
    IEnumerable<IFleetGoalEvaluator> evaluators,
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    IMessageBus bus,
    ILogger<FleetOrchestrator> logger) : IFleetOrchestrator
{
    private const string IdleAssignmentType = "Idle";
    private const string ContractAssignmentType = "Contract";
    private const string BuilderAssignmentType = "Builder";
    private const string MarketProbeAssignmentType = "MarketProbe";

    public async Task EvaluateAndAssignAsync(CancellationToken cancellationToken = default)
    {
        var goals = new List<FleetGoal>();
        foreach (var evaluator in evaluators)
        {
            var produced = await evaluator.EvaluateAsync(cancellationToken);
            goals.AddRange(produced);
        }

        if (goals.Count == 0)
        {
            logger.LogDebug("Orchestrator: no goals produced; nothing to assign.");
            return;
        }

        var orderedGoals = goals
            .OrderByDescending(g => g.Priority)
            .ThenBy(g => g.Deadline ?? DateTimeOffset.MaxValue)
            .ToList();

        var fleet = await ships.GetAllAsync(cancellationToken);
        var activeAssignments = await assignments.GetAllActiveAsync(cancellationToken);
        var assignmentByShip = activeAssignments
            .Where(a => !a.CompletedAt.HasValue)
            .GroupBy(a => a.ShipSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().AssignmentType, StringComparer.OrdinalIgnoreCase);

        var idleShips = fleet
            .Where(s => !assignmentByShip.TryGetValue(s.Symbol, out var t)
                || string.IsNullOrWhiteSpace(t)
                || t.Equals(IdleAssignmentType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (idleShips.Count == 0)
        {
            logger.LogDebug("Orchestrator: no idle ships available for {GoalCount} goals.", orderedGoals.Count);
            return;
        }

        foreach (var goal in orderedGoals)
        {
            if (idleShips.Count == 0)
            {
                break;
            }

            var ship = SelectShipForGoal(goal, idleShips);
            if (ship is null)
            {
                continue;
            }

            var command = BuildAssignment(goal, ship);
            if (command is null)
            {
                continue;
            }

            logger.LogInformation(
                "Orchestrator: assigning ship {Ship} to goal {GoalKind} ({Description}).",
                ship.Symbol,
                goal.Kind,
                goal.Description);

            await bus.SendAsync(command);
            idleShips.Remove(ship);
        }
    }

    private static ShipModel? SelectShipForGoal(FleetGoal goal, IReadOnlyList<ShipModel> idleShips)
    {
        return goal.Kind switch
        {
            FleetGoalKind.Contract or FleetGoalKind.Construction =>
                idleShips.FirstOrDefault(s => s.CargoCapacity > 0 && s.FuelCapacity > 0)
                    ?? idleShips.FirstOrDefault(s => s.CargoCapacity > 0),
            FleetGoalKind.MarketCoverage => idleShips.FirstOrDefault(s => s.FuelCapacity > 0)
                    ?? idleShips.FirstOrDefault(),
            FleetGoalKind.FleetExpansion => null,
            _ => idleShips.FirstOrDefault(),
        };
    }

    private static AssignShipCommand? BuildAssignment(FleetGoal goal, ShipModel ship)
    {
        return goal.Kind switch
        {
            FleetGoalKind.Contract when !string.IsNullOrWhiteSpace(goal.ContractId)
                => new AssignShipCommand(
                    ship.Symbol,
                    ContractAssignmentType,
                    OriginWaypoint: ship.WaypointSymbol,
                    DestWaypoint: goal.DestinationWaypoint,
                    CargoSymbol: goal.TradeSymbol,
                    ContractId: goal.ContractId,
                    RequiredUnits: goal.RemainingUnits),
            FleetGoalKind.Construction when !string.IsNullOrWhiteSpace(goal.DestinationWaypoint)
                => new AssignShipCommand(
                    ship.Symbol,
                    BuilderAssignmentType,
                    OriginWaypoint: ship.WaypointSymbol,
                    DestWaypoint: goal.DestinationWaypoint,
                    CargoSymbol: goal.TradeSymbol,
                    RequiredUnits: goal.RemainingUnits),
            FleetGoalKind.MarketCoverage when !string.IsNullOrWhiteSpace(goal.OriginWaypoint)
                => new AssignShipCommand(
                    ship.Symbol,
                    MarketProbeAssignmentType,
                    OriginWaypoint: goal.OriginWaypoint),
            _ => null,
        };
    }
}
