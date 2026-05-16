using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;

public sealed class PagedApiResponse<T>
{
    [JsonPropertyName("data")]
    required public IReadOnlyList<T> Data { get; init; }

    [JsonPropertyName("meta")]
    required public Meta Meta { get; init; }
}
