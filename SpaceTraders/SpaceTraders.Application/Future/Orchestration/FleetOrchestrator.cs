using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Orchestration;

/// <summary>
/// High-level fleet orchestrator. Aggregates goals from all goal evaluators,
/// matches idle ships to the highest-priority goals, and emits one
/// <see cref="AssignShipToGoalCommand"/> per ship. The orchestrator never issues
/// low-level ship commands (Dock/Orbit/Navigate); ship planners do that.
/// </summary>
public interface IFleetOrchestrator
{
    Task EvaluateAndAssignAsync(CancellationToken cancellationToken = default);
}

public sealed class FleetOrchestrator(
    IEnumerable<IFleetGoalEvaluator> evaluators,
    IShipRepository ships,
    IShipGoalRepository shipGoals,
    IFleetGoalRepository fleetGoals,
    IMessageBus bus,
    ILogger<FleetOrchestrator> logger) : IFleetOrchestrator
{

    public async Task EvaluateAndAssignAsync(CancellationToken cancellationToken = default)
    {
        // Load persisted active goals as the starting set.
        var persistedGoals = await fleetGoals.GetActiveAsync(cancellationToken);
        var persistedByKey = persistedGoals
            .ToDictionary(ComputeNaturalKey, g => g, StringComparer.OrdinalIgnoreCase);

        // Run evaluators and merge with persisted goals.
        var goals = new List<FleetGoal>(persistedGoals);
        foreach (var evaluator in evaluators)
        {
            var produced = await evaluator.EvaluateAsync(cancellationToken);
            foreach (var goal in produced)
            {
                var key = ComputeNaturalKey(goal);
                if (persistedByKey.ContainsKey(key))
                {
                    // Already persisted; do not duplicate.
                    continue;
                }

                // New goal: persist it and add to the working set.
                await fleetGoals.UpsertAsync(goal, cancellationToken);
                goals.Add(goal);
                persistedByKey[key] = goal;
            }
        }

        if (goals.Count == 0)
        {
            logger.LogDebug("Orchestrator: no goals produced; nothing to assign.");
            return;
        }

        var orderedGoals = goals
            .OrderBy(g => g.Priority)
            .ThenBy(g => g.Deadline ?? DateTimeOffset.MaxValue)
            .ToList();

        var fleet = await ships.GetAllAsync(cancellationToken);

        // A ship is idle when it has no active goal or its active goal is the IdleGoal sentinel.
        var shipsWithActiveGoals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ship in fleet)
        {
            var activeGoal = await shipGoals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (activeGoal is not null and not IdleGoal)
            {
                shipsWithActiveGoals.Add(ship.Symbol);
            }
        }

        var idleShips = fleet
            .Where(s => !shipsWithActiveGoals.Contains(s.Symbol))
            .ToList();

        // Fleet expansion goals do not consume an idle ship; dispatch them regardless of idle capacity.
        foreach (var goal in orderedGoals.Where(g => g.Kind == FleetGoalKind.FleetExpansion))
        {
            if (fleet.Count == 0)
            {
                logger.LogDebug("Orchestrator: no ships in fleet; cannot dispatch fleet expansion goal.");
                continue;
            }

            var shipSymbol = fleet[0].Symbol;
            logger.LogInformation(
                "Orchestrator: dispatching {GoalKind} goal ({Description}).",
                goal.Kind,
                goal.Description);
            await bus.SendAsync(new AssignShipToGoalCommand(shipSymbol, goal));
        }

        if (idleShips.Count == 0)
        {
            logger.LogDebug("Orchestrator: no idle ships available for {GoalCount} goals.", orderedGoals.Count);
            return;
        }

        foreach (var goal in orderedGoals.Where(g => g.Kind != FleetGoalKind.FleetExpansion))
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

    /// <summary>
    /// Computes a logical identity key for a fleet goal, used to detect duplicates
    /// between evaluator-produced goals and already-persisted goals.
    /// </summary>
    internal static string ComputeNaturalKey(FleetGoal goal) => goal.Kind switch
    {
        FleetGoalKind.Contract =>
            $"Contract|{goal.ContractId}|{goal.TradeSymbol}",
        FleetGoalKind.Construction =>
            $"Construction|{goal.DestinationWaypoint}|{goal.TradeSymbol}",
        FleetGoalKind.MarketCoverage =>
            $"MarketCoverage|{goal.OriginWaypoint}",
        FleetGoalKind.FleetExpansion =>
            $"FleetExpansion|{goal.ExpansionShipType}|{goal.ExpansionShipyardWaypoint}",
        _ => $"{goal.Kind}|{goal.Description}",
    };

    private static ShipModel? SelectShipForGoal(FleetGoal goal, IReadOnlyList<ShipModel> idleShips)
    {
        return goal.Kind switch
        {
            FleetGoalKind.Contract or FleetGoalKind.Construction =>
                idleShips.FirstOrDefault(s => s.CargoCapacity > 0 && s.FuelCapacity > 0)
                    ?? idleShips.FirstOrDefault(s => s.CargoCapacity > 0),
            FleetGoalKind.MarketCoverage => idleShips.FirstOrDefault(s => s.FuelCapacity > 0)
                    ?? idleShips.FirstOrDefault(),
            _ => idleShips.FirstOrDefault(),
        };
    }

    private static AssignShipToGoalCommand? BuildAssignment(FleetGoal goal, ShipModel ship)
    {
        return goal.Kind switch
        {
            FleetGoalKind.Contract when !string.IsNullOrWhiteSpace(goal.ContractId)
                => new AssignShipToGoalCommand(ship.Symbol, goal),
            FleetGoalKind.Construction when !string.IsNullOrWhiteSpace(goal.DestinationWaypoint)
                => new AssignShipToGoalCommand(ship.Symbol, goal),
            FleetGoalKind.MarketCoverage when !string.IsNullOrWhiteSpace(goal.OriginWaypoint)
                => new AssignShipToGoalCommand(ship.Symbol, goal),
            FleetGoalKind.MarketScouting when !string.IsNullOrWhiteSpace(goal.OriginWaypoint)
                => new AssignShipToGoalCommand(ship.Symbol, goal),
            _ => null,
        };
    }
}
