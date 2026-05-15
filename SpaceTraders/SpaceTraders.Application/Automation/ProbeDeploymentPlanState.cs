namespace SpaceTraders.Application.Automation;

public enum ProbeDeploymentPlanStatus
{
    None = 0,
    Active = 1,
    Completed = 2,
}

/// <summary>
/// Persistent state for the fleet-level probe deployment plan.
/// Tracks which waypoints require a probe and which probes have already been dispatched.
/// </summary>
/// <remarks>
/// <see cref="ShipyardWaypointSymbols"/> is the Phase 0 subset (targets that have a shipyard).
/// All remaining targets in <see cref="TargetWaypointSymbols"/> are Phase 1+ (market-only).
/// </remarks>
public sealed record ProbeDeploymentPlanState
{
    public required Guid PlanId { get; init; }

    public required string SystemSymbol { get; init; }

    /// <summary>All waypoints targeted for probe deployment (shipyards + markets).</summary>
    public required IReadOnlyList<string> TargetWaypointSymbols { get; init; }

    /// <summary>Subset of <see cref="TargetWaypointSymbols"/> that have a shipyard (Phase 0 targets).</summary>
    /// <remarks>May be <c>null</c> for plans persisted before this field was introduced; the service treats null as empty.</remarks>
    public IReadOnlyList<string>? ShipyardWaypointSymbols { get; init; }

    public required IReadOnlyList<string> DeployedWaypointSymbols { get; init; }

    public required ProbeDeploymentPlanStatus Status { get; init; }

    /// <summary>
    /// Indicates Phase 1 was deferred due to insufficient credits and should remain dormant
    /// until an <c>AgentCreditsChangedEvent</c> re-triggers evaluation.
    /// </summary>
    public bool WaitingForPhase1Credits { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
