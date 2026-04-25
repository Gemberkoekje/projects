using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;

public sealed class PublicAgent
{
    [JsonPropertyName("accountId")]
    public string? AccountId { get; init; }

    [JsonPropertyName("symbol")]
    public required string Symbol { get; init; }

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; init; }

    [JsonPropertyName("credits")]
    public long Credits { get; init; }

    [JsonPropertyName("startingFaction")]
    public required string StartingFaction { get; init; }

    [JsonPropertyName("shipCount")]
    public int ShipCount { get; init; }
}
