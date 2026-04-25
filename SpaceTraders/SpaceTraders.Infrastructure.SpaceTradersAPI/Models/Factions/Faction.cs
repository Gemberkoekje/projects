using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Factions;

public sealed class Faction
{
    [JsonPropertyName("symbol")]
    public required string Symbol { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; init; }

    [JsonPropertyName("isRecruiting")]
    public bool IsRecruiting { get; init; }
}
