namespace SpaceTraders.Infrastructure.Persistence.Entities;

[Mutable]
public sealed class CachedConstruction
{
    public string AgentId { get; init; } = string.Empty;

    required public string WaypointSymbol { get; init; }

    required public string SystemSymbol { get; init; }

    public bool IsComplete { get; set; }

    public string? MaterialsJson { get; set; }

    public DateTimeOffset LastObservedAt { get; set; }
}
