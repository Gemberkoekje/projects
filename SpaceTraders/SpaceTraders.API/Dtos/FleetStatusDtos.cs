namespace SpaceTraders.API.Dtos;

/// <summary>
/// JSON-serializable DTO for a single active fleet goal chain, including per-resource delivery progress.
/// Enum fields from the domain model are mapped to strings for a stable, versioning-friendly wire format.
/// </summary>
/// <remarks>Phase 16b: response DTO mapping.</remarks>
public sealed record FleetGoalChainDto
{
    public required Guid FleetGoalId { get; init; }

    /// <summary>Fleet goal kind as a string, e.g. "Contract", "Construction".</summary>
    public required string FleetGoalKind { get; init; }

    public required int Priority { get; init; }

    public required string FleetGoalDescription { get; init; }

    public required IReadOnlyList<ResourceNeedEntryDto> ResourceNeeds { get; init; }
}

/// <summary>
/// JSON-serializable DTO for a single resource requirement within a fleet goal chain.
/// </summary>
/// <remarks>Phase 16b: response DTO mapping.</remarks>
public sealed record ResourceNeedEntryDto
{
    public required string TradeSymbol { get; init; }

    public required int UnitsNeeded { get; init; }

    public required int UnitsDelivered { get; init; }

    public required string PurposeDescription { get; init; }

    public required IReadOnlyList<string> AssignedShips { get; init; }
}

/// <summary>
/// JSON-serializable DTO for a ship's current assignment snapshot.
/// Enum fields are mapped to strings; <see cref="AssignedAt"/> is ISO 8601.
/// </summary>
/// <remarks>Phase 16b: response DTO mapping.</remarks>
public sealed record ShipAssignmentDto
{
    public required string ShipSymbol { get; init; }

    /// <summary>Ship goal kind as a string, e.g. "Idle", "MineResource".</summary>
    public required string GoalKind { get; init; }

    public required string GoalDescription { get; init; }

    public string? SourceWaypoint { get; init; }

    public string? DestinationWaypoint { get; init; }

    public Guid? FleetGoalId { get; init; }

    public string? FleetGoalDescription { get; init; }

    /// <summary>ISO 8601 timestamp of when the ship was assigned to its current goal.</summary>
    public DateTimeOffset? AssignedAt { get; init; }
}

/// <summary>
/// JSON-serializable DTO for a single completed or blocked goal in the per-ship goal history.
/// </summary>
/// <remarks>Phase 17f: introduced as part of the fleet dashboard implementation.</remarks>
public sealed record ShipGoalHistoryDto
{
    public required Guid Id { get; init; }

    public required Guid GoalId { get; init; }

    public required string GoalKind { get; init; }

    /// <summary>'Completed' or 'Blocked'.</summary>
    public required string Outcome { get; init; }

    /// <summary>Blocking reason when <see cref="Outcome"/> is 'Blocked'; <c>null</c> otherwise.</summary>
    public string? Reason { get; init; }

    /// <summary>ISO 8601 timestamp of when the goal started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>ISO 8601 timestamp of when the goal ended.</summary>
    public required DateTimeOffset EndedAt { get; init; }

    /// <summary>Total positive credits recorded while the goal was active.</summary>
    public required long CreditsEarned { get; init; }

    /// <summary>Total credits spent while the goal was active, expressed as a positive number.</summary>
    public required long CreditsSpent { get; init; }

    /// <summary>Net credit impact while the goal was active.</summary>
    public required long NetCredits { get; init; }

    /// <summary>Number of ledger entries recorded while the goal was active.</summary>
    public required int LedgerEntryCount { get; init; }

    /// <summary>Duration of the goal in whole seconds.</summary>
    public required long DurationSeconds { get; init; }
}

/// <summary>
/// JSON-serializable DTO for a ship's live activity snapshot.
/// Enum fields are mapped to strings; <see cref="EstimatedArrival"/> and
/// <see cref="CooldownExpiresAt"/> are ISO 8601.
/// </summary>
/// <remarks>Phase 16b: response DTO mapping.</remarks>
public sealed record ShipActivityDto
{
    public required string ShipSymbol { get; init; }

    /// <summary>Navigation status as a string, e.g. "Docked", "InTransit", "InOrbit".</summary>
    public required string LocalStatus { get; init; }

    public string? CurrentWaypoint { get; init; }

    public string? DestinationWaypoint { get; init; }

    /// <summary>ISO 8601 timestamp of when the ship will arrive at its destination.</summary>
    public DateTimeOffset? EstimatedArrival { get; init; }

    public required bool OnCooldown { get; init; }

    /// <summary>ISO 8601 timestamp of when the cooldown expires.</summary>
    public DateTimeOffset? CooldownExpiresAt { get; init; }

    public required int CargoUsed { get; init; }

    public required int CargoCapacity { get; init; }

    public required int FuelCurrent { get; init; }

    public required int FuelCapacity { get; init; }

    public required string ActivityDescription { get; init; }
}

/// <summary>
/// JSON-serializable DTO for detailed waypoint information.
/// </summary>
public sealed record WaypointDetailDto
{
    public required string Symbol { get; init; }

    public required string SystemSymbol { get; init; }

    public required string Type { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }

    public required bool HasMarket { get; init; }

    public required bool HasShipyard { get; init; }

    public required bool IsUnderConstruction { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    public string? ParentSymbol { get; init; }

    public string? TraitsJson { get; init; }

    public string? ModifiersJson { get; init; }

    public string? OrbitalsJson { get; init; }

    public string? ChartJson { get; init; }

    public required IReadOnlyList<string> ExtractableResources { get; init; }
}
