using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;

public sealed class RegisterRequest
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("faction")]
    required public string Faction { get; init; }
}
