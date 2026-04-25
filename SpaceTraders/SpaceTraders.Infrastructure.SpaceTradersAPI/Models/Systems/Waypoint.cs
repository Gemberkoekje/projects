using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems;

public sealed class Waypoint
{
    [JsonPropertyName("symbol")]
    public required string Symbol { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("systemSymbol")]
    public required string SystemSymbol { get; init; }

    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("orbitals")]
    public IReadOnlyList<WaypointOrbital>? Orbitals { get; init; }
}

public sealed class WaypointOrbital
{
    [JsonPropertyName("symbol")]
    public required string Symbol { get; init; }
}
