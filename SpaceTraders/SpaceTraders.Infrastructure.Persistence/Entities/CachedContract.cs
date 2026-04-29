namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedContract
{
    public string AgentToken { get; init; } = string.Empty;

    required public string Id { get; init; }

    required public string FactionSymbol { get; init; }

    required public string Type { get; init; }

    public bool IsAccepted { get; init; }

    public bool IsFulfilled { get; init; }

    public DateTimeOffset? Expiration { get; init; }

    public DateTimeOffset? DeadlineToAccept { get; init; }

    public DateTimeOffset? TermsDeadline { get; init; }

    public string? DeliverablesJson { get; init; }

    public DateTimeOffset LastSyncedAt { get; init; }
}
