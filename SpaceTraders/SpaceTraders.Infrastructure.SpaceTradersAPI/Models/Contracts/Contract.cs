using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;

public sealed class Contract
{
    [JsonPropertyName("id")]
    required public string Id { get; init; }

    [JsonPropertyName("factionSymbol")]
    required public string FactionSymbol { get; init; }

    [JsonPropertyName("type")]
    required public string Type { get; init; }

    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("fulfilled")]
    public bool Fulfilled { get; init; }

    [JsonPropertyName("expiration")]
    public DateTimeOffset? Expiration { get; init; }

    [JsonPropertyName("deadlineToAccept")]
    public DateTimeOffset? DeadlineToAccept { get; init; }
}
