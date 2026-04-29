namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedSurvey
{
    public string AgentToken { get; init; } = string.Empty;

    required public string Signature { get; init; }

    required public string ShipSymbol { get; init; }

    required public string WaypointSymbol { get; init; }

    public string DepositsJson { get; init; } = "[]";

    public DateTimeOffset Expiration { get; init; }

    required public string Size { get; init; }

    public DateTimeOffset RecordedAt { get; init; }
}
