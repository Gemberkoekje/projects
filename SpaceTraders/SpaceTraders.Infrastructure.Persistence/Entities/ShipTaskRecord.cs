namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class ShipTaskRecord
{
    public long Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    required public string ShipSymbol { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; set; }

    required public string TaskKind { get; init; }

    public string? TargetWaypoint { get; init; }

    public string? PayloadJson { get; init; }
}
