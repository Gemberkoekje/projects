namespace SpaceTraders.Application.Goals;

/// <summary>
/// Read-only contextual data populated by <see cref="ShipGoalExecutorService"/> and consumed by
/// goal executors. Analogous to <c>ShipPlannerContext</c> but shaped for goal-based execution.
/// </summary>
/// <remarks>Phase 10: introduced as part of the goal-driven architecture migration.</remarks>
public sealed record ShipGoalContext
{
    /// <summary>Number of active surveys at the ship's current waypoint.</summary>
    public int ActiveSurveyCount { get; init; }

    /// <summary>Symbol of the nearest market waypoint in the ship's system that sells FUEL. Empty when none is cached.</summary>
    public string FuelMarketWaypoint { get; init; } = string.Empty;

    /// <summary>True when the ship is docked at a waypoint whose cached market data includes FUEL.</summary>
    public bool CurrentWaypointSellsFuel { get; init; }

    /// <summary>Recommended flight mode for the primary navigation target of the current goal. Empty when not applicable.</summary>
    public string RecommendedFlightMode { get; init; } = string.Empty;
}
