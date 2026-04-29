using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems;

public sealed class JumpGate
{
    [JsonPropertyName("symbol")]
    required public string Symbol { get; init; }

    [JsonPropertyName("connections")]
    public IReadOnlyList<string>? Connections { get; init; }
}
