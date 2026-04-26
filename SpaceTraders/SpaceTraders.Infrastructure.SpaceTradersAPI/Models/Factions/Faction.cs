using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Factions;

public sealed class Faction
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("name")]
    required public string Name { get; init; }

    [JsonPropertyName("description")]
    required public string Description { get; init; }

    [JsonPropertyName("headquarters")]
    public string? Headquarters { get; init; }

    [JsonPropertyName("isRecruiting")]
    public bool IsRecruiting { get; init; }
}
