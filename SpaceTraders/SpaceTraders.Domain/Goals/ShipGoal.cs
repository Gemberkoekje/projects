using System.Text.Json.Serialization;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Domain.Goals;

/// <summary>
/// Base record for all ship goals. A goal is a complete, self-contained objective for a single ship.
/// Each subtype carries the concrete parameters the executor needs; the ship handles all prerequisite
/// actions (refuel, navigate, handle cooldown) to reach the goal without external coordination.
/// </summary>
/// <remarks>
/// Phase 8: goal model introduced as part of the goal-driven architecture migration.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(IdleGoal), "Idle")]
[JsonDerivedType(typeof(MoveToWaypointGoal), "MoveToWaypoint")]
[JsonDerivedType(typeof(MineResourceGoal), "MineResource")]
[JsonDerivedType(typeof(SiphonResourceGoal), "SiphonResource")]
[JsonDerivedType(typeof(SellCargoGoal), "SellCargo")]
[JsonDerivedType(typeof(DeliverCargoGoal), "DeliverCargo")]
[JsonDerivedType(typeof(SupplyConstructionGoal), "SupplyConstruction")]
[JsonDerivedType(typeof(ScoutWaypointGoal), "ScoutWaypoint")]
[JsonDerivedType(typeof(PatrolMarketGoal), "PatrolMarket")]
public abstract record ShipGoal
{
    /// <summary>Correlation token that links orchestrator assignment, goal execution, and completion events.</summary>
    public Guid GoalId { get; init; } = Guid.NewGuid();

    /// <summary>Current lifecycle status of this goal.</summary>
    public GoalStatus Status { get; init; } = GoalStatus.Assigned;

    /// <summary>Discriminator for the goal type; derived from the concrete subtype.</summary>
    [JsonIgnore]
    public abstract ShipGoalKind Kind { get; }
}

/// <summary>The ship has no active objective and is available for a new assignment.</summary>
public sealed record IdleGoal : ShipGoal
{
    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.Idle;
}

/// <summary>The ship navigates to a target waypoint without any further objective.</summary>
public sealed record MoveToWaypointGoal : ShipGoal
{
    public required string TargetWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.MoveToWaypoint;
}

/// <summary>The ship extracts a specific trade good from a source asteroid waypoint.</summary>
public sealed record MineResourceGoal : ShipGoal
{
    public required string TradeSymbol { get; init; }

    public required string SourceWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.MineResource;
}

/// <summary>The ship siphons a specific trade good from a gas-giant waypoint.</summary>
public sealed record SiphonResourceGoal : ShipGoal
{
    public required string TradeSymbol { get; init; }

    public required string SourceWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.SiphonResource;
}

/// <summary>The ship travels to a destination market and sells the listed trade goods.</summary>
public sealed record SellCargoGoal : ShipGoal
{
    public required string DestinationWaypointSymbol { get; init; }

    public required IReadOnlyList<string> TradeSymbols { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.SellCargo;
}

/// <summary>The ship delivers a specific trade good to a contract delivery waypoint.</summary>
public sealed record DeliverCargoGoal : ShipGoal
{
    public required string ContractId { get; init; }

    public required string TradeSymbol { get; init; }

    public required string DeliveryWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.DeliverCargo;
}

/// <summary>The ship delivers a trade good to an active construction site.</summary>
public sealed record SupplyConstructionGoal : ShipGoal
{
    public required string TradeSymbol { get; init; }

    public required string ConstructionSiteWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.SupplyConstruction;
}

/// <summary>The ship travels to a target waypoint to chart it and refresh its market / waypoint data.</summary>
public sealed record ScoutWaypointGoal : ShipGoal
{
    public required string TargetWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.ScoutWaypoint;
}

/// <summary>The ship repeatedly visits a market waypoint to keep market data fresh.</summary>
public sealed record PatrolMarketGoal : ShipGoal
{
    public required string TargetWaypointSymbol { get; init; }

    [JsonIgnore]
    public override ShipGoalKind Kind => ShipGoalKind.PatrolMarket;
}
