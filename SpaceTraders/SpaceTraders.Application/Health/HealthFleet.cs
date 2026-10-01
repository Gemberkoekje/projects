using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Health;

/// <summary>Loads the fleet as the health rules see it: each ship with its goal and its open assignment.</summary>
public sealed class HealthFleet(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipAssignmentRepository assignments)
{
    /// <summary>Loads every cached ship, its active goal and its open assignment.</summary>
    /// <param name="cancellationToken">Stops the load.</param>
    /// <returns>The fleet, one entry per ship.</returns>
    public async Task<IReadOnlyList<FleetShip>> LoadAsync(CancellationToken cancellationToken)
    {
        var fleet = await ships.GetAllAsync(cancellationToken);
        var open = (await assignments.GetAllActiveAsync(cancellationToken))
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .GroupBy(assignment => assignment.ShipSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<FleetShip>(fleet.Count);
        foreach (var ship in fleet)
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            result.Add(new FleetShip(ship, goal, open.GetValueOrDefault(ship.Symbol)));
        }

        return result;
    }
}

/// <summary>A ship, its active goal and its open assignment.</summary>
public sealed record FleetShip
{
    private const string ContractAssignmentType = "Contract";

    /// <summary>Creates the view of one ship.</summary>
    /// <param name="Ship">The cached ship.</param>
    /// <param name="Goal">Its active goal, if it has one.</param>
    /// <param name="Assignment">Its open assignment, if it has one.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public FleetShip(ShipModel Ship, ShipGoal? Goal, ShipAssignmentDto? Assignment)
    {
        this.Ship = Ship;
        this.Goal = Goal;
        this.Assignment = Assignment;
    }

    /// <summary>The cached ship.</summary>
    public required ShipModel Ship { get; init; }

    /// <summary>Its active goal; <c>null</c> when it has none.</summary>
    public required ShipGoal? Goal { get; init; }

    /// <summary>Its open assignment (scout or contract); <c>null</c> when it has none.</summary>
    public required ShipAssignmentDto? Assignment { get; init; }

    /// <summary>The ship's symbol.</summary>
    public string Symbol => Ship.Symbol;

    /// <summary>Whether it has neither a goal nor an open assignment, as the journal's <c>ShipIdle</c> counts it.</summary>
    public bool IsIdle => Goal is null && Assignment is null;

    /// <summary>Whether its goal was blocked, by the circuit breaker (<c>runaway</c>).</summary>
    public bool IsBlocked => Goal?.Status == GoalStatus.Blocked;

    /// <summary>Whether it is still travelling at <paramref name="now"/>, with arrivals dead-reckoned.</summary>
    /// <param name="now">The time to judge by.</param>
    /// <returns><c>true</c> before the ship's arrival time.</returns>
    public bool InTransitAt(DateTimeOffset now) => Ship.ArrivesAt > now;

    /// <summary>
    /// The plans whose work the ship has: the plan that gave its goal (unless the goal is blocked or
    /// done), and the contract plan for a contract assignment.
    /// </summary>
    public IReadOnlyList<AutomationPlan> WorkPlans
    {
        get
        {
            var plans = new List<AutomationPlan>(2);
            if (Goal is { Status: not GoalStatus.Blocked and not GoalStatus.Completed }
                && AutomationSwitches.PlanFor(Goal) is { } goalPlan)
            {
                plans.Add(goalPlan);
            }

            if (Assignment is not null
                && Assignment.AssignmentType.Equals(ContractAssignmentType, StringComparison.OrdinalIgnoreCase))
            {
                plans.Add(AutomationPlan.Contract);
            }

            return plans;
        }
    }

    /// <summary>What the ship works on, for the journal: its goal's kind, else its assignment's type.</summary>
    public string Work => Goal is not null ? $"{Goal.Kind} goal" : $"{Assignment?.AssignmentType} assignment";

    /// <summary>Where the ship is and in what state, for the journal: <c>IN_ORBIT at X1-AB-C1</c>.</summary>
    public string Whereabouts => $"{Ship.Status ?? "UNKNOWN"} at {Ship.WaypointSymbol ?? "an unknown waypoint"}";
}

/// <summary>The health rules' thresholds, which are settings.</summary>
internal static class HealthSettings
{
    /// <summary>Reads a whole-number threshold; a missing, invalid, zero or negative value means <paramref name="defaultValue"/>.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="key">The setting's key.</param>
    /// <param name="defaultValue">The threshold when the setting gives none.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The threshold.</returns>
    public static async Task<int> ThresholdAsync(this ISettingsRepository settings, string key, int defaultValue, CancellationToken cancellationToken)
    {
        var configured = await settings.GetAsync<int>(key, cancellationToken);
        return configured > 0 ? configured : defaultValue;
    }
}
