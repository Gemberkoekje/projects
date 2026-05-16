using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;

public sealed class ApiResponse<T>
{
    [JsonPropertyName("data")]
    required public T Data { get; init; }
}
