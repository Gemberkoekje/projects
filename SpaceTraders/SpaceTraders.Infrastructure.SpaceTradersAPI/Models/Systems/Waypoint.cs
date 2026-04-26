using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems;

public sealed class Waypoint
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("type")]
    required public string Type { get; init; }

    [JsonPropertyName("systemSymbol")]
    required public string SystemSymbol { get; init; }

    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("orbitals")]
    public IReadOnlyList<WaypointOrbital>? Orbitals { get; init; }

    [JsonPropertyName("traits")]
    public IReadOnlyList<WaypointTrait>? Traits { get; init; }
}

public sealed class WaypointOrbital
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }
}

public sealed class WaypointTrait
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }
}
