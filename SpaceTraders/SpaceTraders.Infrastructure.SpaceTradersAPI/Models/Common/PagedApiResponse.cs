using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;

public sealed class PagedApiResponse<T>
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<T> Data { get; init; }

    [JsonPropertyName("meta")]
    public required Meta Meta { get; init; }
}
