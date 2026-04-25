using System.Text.Json.Serialization;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Factions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;

public sealed class RegisterResponseData
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("agent")]
    public required Agent Agent { get; init; }

    [JsonPropertyName("faction")]
    public required Faction Faction { get; init; }

    [JsonPropertyName("contract")]
    public required Contract Contract { get; init; }

    [JsonPropertyName("ships")]
    public required IReadOnlyList<Ship> Ships { get; init; }
}
