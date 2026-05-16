using System.Text.Json.Serialization;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Factions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;

public sealed class RegisterResponseData
{
    [JsonPropertyName("token")]
    required public string Token { get; init; }

    [JsonPropertyName("agent")]
    required public Agent Agent { get; init; }

    [JsonPropertyName("faction")]
    required public Faction Faction { get; init; }

    [JsonPropertyName("contract")]
    required public Contract Contract { get; init; }

    [JsonPropertyName("ships")]
    required public IReadOnlyList<Ship> Ships { get; init; }
}
