namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class CachedSurvey
{
    public string AgentId { get; init; } = string.Empty;

    required public string Signature { get; init; }

    required public string ShipSymbol { get; init; }

    required public string WaypointSymbol { get; init; }

    public string DepositsJson { get; init; } = "[]";

    public DateTimeOffset Expiration { get; init; }

    required public string Size { get; init; }

    public DateTimeOffset RecordedAt { get; init; }

    /// <summary>How many extractions were made with the survey (slice 6.4): the survey dashboard shows which went unused.</summary>
    public int Extractions { get; init; }
}
