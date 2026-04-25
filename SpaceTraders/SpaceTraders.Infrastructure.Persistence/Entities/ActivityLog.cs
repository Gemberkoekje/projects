namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class ActivityLog
{
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public required string ShipSymbol { get; set; }

    public required string EventType { get; set; }

    public required string Message { get; set; }

    public string? JsonDetails { get; set; }
}
