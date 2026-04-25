using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems;

public sealed class SystemInfo
{
    [JsonPropertyName("symbol")]
    public required string Symbol { get; init; }

    [JsonPropertyName("sectorSymbol")]
    public required string SectorSymbol { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }
}
