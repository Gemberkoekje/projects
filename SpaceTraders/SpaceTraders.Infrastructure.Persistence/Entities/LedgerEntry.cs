using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class LedgerEntry
{
    public long Id { get; init; }

    public string AgentToken { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    required public string ShipSymbol { get; init; }

    public Guid? RunId { get; init; }

    public LedgerCategory Category { get; init; }

    public long Amount { get; init; }

    public string? GoodSymbol { get; init; }

    public int? UnitPrice { get; init; }

    public int? Units { get; init; }

    public string? WaypointSymbol { get; init; }

    public string? SourceEventId { get; init; }
}
