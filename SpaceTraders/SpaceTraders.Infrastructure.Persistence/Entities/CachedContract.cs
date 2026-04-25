namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedContract
{
    public required string Id { get; set; }

    public required string FactionSymbol { get; set; }

    public required string Type { get; set; }

    public bool IsAccepted { get; set; }

    public bool IsFulfilled { get; set; }

    public DateTimeOffset? Expiration { get; set; }

    public DateTimeOffset? DeadlineToAccept { get; set; }

    public DateTimeOffset LastSyncedAt { get; set; }
}
