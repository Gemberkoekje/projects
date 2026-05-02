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
