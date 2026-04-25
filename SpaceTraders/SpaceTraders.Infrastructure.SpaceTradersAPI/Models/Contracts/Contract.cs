using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;

public sealed class Contract
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("factionSymbol")]
    public required string FactionSymbol { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("fulfilled")]
    public bool Fulfilled { get; init; }

    [JsonPropertyName("expiration")]
    public DateTimeOffset? Expiration { get; init; }

    [JsonPropertyName("deadlineToAccept")]
    public DateTimeOffset? DeadlineToAccept { get; init; }
}
