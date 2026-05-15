using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Translates an abstract <see cref="ResourceProductionGoal"/> into a concrete <see cref="ShipGoal"/>
/// with specific waypoints. The resolver is the only layer that performs spatial decisions: it queries
/// waypoint and market repositories so that the orchestrator and goal executors remain waypoint-agnostic.
/// </summary>
/// <remarks>Phase 9: introduced as part of the goal-driven architecture migration.</remarks>
public interface IAssignmentResolver
{
    /// <summary>
    /// Resolves a concrete <see cref="ShipGoal"/> for the given ship and abstract resource production goal.
    /// Returns <c>null</c> when no suitable source waypoint can be found and the assignment is unresolvable.
    /// </summary>
    Task<ShipGoal?> ResolveAsync(
        ShipModel ship,
        ResourceProductionGoal goal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the symbol of the nearest market waypoint (in the ship's current system) that imports or
    /// exchanges the given trade symbol, or <c>null</c> when no matching market is cached.
    /// </summary>
    Task<string?> FindNearestSellMarketAsync(
        ShipModel ship,
        string tradeSymbol,
        CancellationToken cancellationToken = default);

    Task<string?> FindBestSellMarketAsync(
        ShipModel ship,
        string tradeSymbol,
        string? preferredWaypoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a <see cref="ShipGoal"/> for a <see cref="FleetGoalKind.MarketCoverage"/> assignment.
    /// Returns a <see cref="ScoutWaypointGoal"/> when no cached market snapshot exists for the waypoint,
    /// or a <see cref="PatrolMarketGoal"/> when the market has already been scouted.
    /// </summary>
    Task<ShipGoal> ResolveMarketCoverageAsync(
        string waypointSymbol,
        CancellationToken cancellationToken = default);
}
