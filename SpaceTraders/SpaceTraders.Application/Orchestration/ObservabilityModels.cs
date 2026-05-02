using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Orchestration;

/// <summary>
/// A point-in-time snapshot of a single active fleet goal and its associated resource needs.
/// Used by the observability / dashboard layer to display what the orchestrator is working towards.
/// </summary>
/// <remarks>Phase 15a: introduced as part of the observability read-model layer.</remarks>
public sealed record OrchestratorGoalChain
{
    /// <summary>Stable identifier of the backing <see cref="FleetGoal"/>.</summary>
    public required Guid FleetGoalId { get; init; }

    /// <summary>The kind of the fleet goal (Contract, Construction, MarketScouting, …).</summary>
    public required FleetGoalKind FleetGoalKind { get; init; }

    /// <summary>Ascending priority value; lower numbers are worked on first.</summary>
    public required int Priority { get; init; }

    /// <summary>Human-readable description, e.g. "Supply Jump Gate at X1-TD7-JG".</summary>
    public required string FleetGoalDescription { get; init; }

    /// <summary>
    /// Per-resource breakdown for <see cref="FleetGoalKind.Contract"/> and
    /// <see cref="FleetGoalKind.Construction"/> goals.
    /// Empty for all other goal kinds.
    /// </summary>
    public required IReadOnlyList<ResourceNeedEntry> ResourceNeeds { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public OrchestratorGoalChain(
        Guid FleetGoalId,
        FleetGoalKind FleetGoalKind,
        int Priority,
        string FleetGoalDescription,
        IReadOnlyList<ResourceNeedEntry> ResourceNeeds)
    {
        this.FleetGoalId = FleetGoalId;
        this.FleetGoalKind = FleetGoalKind;
        this.Priority = Priority;
        this.FleetGoalDescription = FleetGoalDescription;
        this.ResourceNeeds = ResourceNeeds;
    }
}

/// <summary>
/// A single resource requirement within an <see cref="OrchestratorGoalChain"/>,
/// with delivery progress and the ships currently assigned to satisfy it.
/// </summary>
/// <remarks>Phase 15a: introduced as part of the observability read-model layer.</remarks>
public sealed record ResourceNeedEntry
{
    /// <summary>SpaceTraders trade symbol, e.g. "BAUXITE".</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>Total units required to satisfy the goal.</summary>
    public required int UnitsNeeded { get; init; }

    /// <summary>Units that have already been delivered.</summary>
    public required int UnitsDelivered { get; init; }

    /// <summary>
    /// Human-readable explanation, e.g. "needed for FRAME_DRONE × 2 to build Jump Gate".
    /// </summary>
    public required string PurposeDescription { get; init; }

    /// <summary>Ship symbols currently assigned to collect or deliver this resource.</summary>
    public required IReadOnlyList<string> AssignedShips { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ResourceNeedEntry(
        string TradeSymbol,
        int UnitsNeeded,
        int UnitsDelivered,
        string PurposeDescription,
        IReadOnlyList<string> AssignedShips)
    {
        this.TradeSymbol = TradeSymbol;
        this.UnitsNeeded = UnitsNeeded;
        this.UnitsDelivered = UnitsDelivered;
        this.PurposeDescription = PurposeDescription;
        this.AssignedShips = AssignedShips;
    }
}

/// <summary>
/// A point-in-time snapshot of a single ship's current assignment.
/// Captures the ship's active <see cref="ShipGoalKind"/>, the waypoints it is working
/// between, and — where available — the fleet goal it is serving.
/// </summary>
/// <remarks>Phase 15c: introduced as part of the observability read-model layer.</remarks>
public sealed record ShipAssignmentSnapshot
{
    /// <summary>Unique identifier of the ship, e.g. "X1-TD7-1".</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>The kind of ship goal currently active (Idle when nothing is assigned).</summary>
    public required ShipGoalKind GoalKind { get; init; }

    /// <summary>Human-readable description, e.g. "Mining BAUXITE at X1-TD7-A3".</summary>
    public required string GoalDescription { get; init; }

    /// <summary>Origin waypoint for the current step (may be null for stationary goals).</summary>
    public string? SourceWaypoint { get; init; }

    /// <summary>Destination waypoint for the current step (may be null for stationary goals).</summary>
    public string? DestinationWaypoint { get; init; }

    /// <summary>The fleet goal this ship is serving, if any.</summary>
    public Guid? FleetGoalId { get; init; }

    /// <summary>Human-readable description of the parent fleet goal, if any.</summary>
    public string? FleetGoalDescription { get; init; }

    /// <summary>When the ship was assigned to its current goal.</summary>
    public required DateTimeOffset AssignedAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipAssignmentSnapshot(
        string ShipSymbol,
        ShipGoalKind GoalKind,
        string GoalDescription,
        DateTimeOffset AssignedAt,
        string? SourceWaypoint = null,
        string? DestinationWaypoint = null,
        Guid? FleetGoalId = null,
        string? FleetGoalDescription = null)
    {
        this.ShipSymbol = ShipSymbol;
        this.GoalKind = GoalKind;
        this.GoalDescription = GoalDescription;
        this.AssignedAt = AssignedAt;
        this.SourceWaypoint = SourceWaypoint;
        this.DestinationWaypoint = DestinationWaypoint;
        this.FleetGoalId = FleetGoalId;
        this.FleetGoalDescription = FleetGoalDescription;
    }
}

/// <summary>
/// A point-in-time snapshot of a ship's live physical state: nav status, cargo, fuel,
/// and cooldown. Used by the dashboard to show what each ship is currently doing at a
/// moment-in-time level below the goal layer.
/// </summary>
/// <remarks>Phase 15e: introduced as part of the observability read-model layer.</remarks>
public sealed record ShipActivitySnapshot
{
    /// <summary>Unique identifier of the ship.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>Current navigation status of the ship.</summary>
    public required ShipLocalStatus LocalStatus { get; init; }

    /// <summary>The waypoint the ship is currently at (null when in transit).</summary>
    public string? CurrentWaypoint { get; init; }

    /// <summary>The waypoint the ship is travelling towards (set when in transit).</summary>
    public string? DestinationWaypoint { get; init; }

    /// <summary>When the ship will arrive at its destination (set when in transit).</summary>
    public DateTimeOffset? EstimatedArrival { get; init; }

    /// <summary>Whether the ship is currently on cooldown (e.g. after extraction).</summary>
    public required bool OnCooldown { get; init; }

    /// <summary>When the cooldown expires (null when not on cooldown).</summary>
    public DateTimeOffset? CooldownExpiresAt { get; init; }

    /// <summary>Number of cargo units currently loaded.</summary>
    public required int CargoUsed { get; init; }

    /// <summary>Maximum cargo capacity of the ship.</summary>
    public required int CargoCapacity { get; init; }

    /// <summary>Current fuel level.</summary>
    public required int FuelCurrent { get; init; }

    /// <summary>Maximum fuel capacity of the ship.</summary>
    public required int FuelCapacity { get; init; }

    /// <summary>
    /// Human-readable summary of what the ship is currently doing,
    /// e.g. "Moving to X1-TD7-A3 (arrives 14:23 UTC)".
    /// </summary>
    public required string ActivityDescription { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipActivitySnapshot(
        string ShipSymbol,
        ShipLocalStatus LocalStatus,
        string ActivityDescription,
        int CargoUsed,
        int CargoCapacity,
        int FuelCurrent,
        int FuelCapacity,
        bool OnCooldown,
        string? CurrentWaypoint = null,
        string? DestinationWaypoint = null,
        DateTimeOffset? EstimatedArrival = null,
        DateTimeOffset? CooldownExpiresAt = null)
    {
        this.ShipSymbol = ShipSymbol;
        this.LocalStatus = LocalStatus;
        this.ActivityDescription = ActivityDescription;
        this.CargoUsed = CargoUsed;
        this.CargoCapacity = CargoCapacity;
        this.FuelCurrent = FuelCurrent;
        this.FuelCapacity = FuelCapacity;
        this.OnCooldown = OnCooldown;
        this.CurrentWaypoint = CurrentWaypoint;
        this.DestinationWaypoint = DestinationWaypoint;
        this.EstimatedArrival = EstimatedArrival;
        this.CooldownExpiresAt = CooldownExpiresAt;
    }
}
