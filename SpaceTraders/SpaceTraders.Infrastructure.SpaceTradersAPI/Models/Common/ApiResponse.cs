using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;

public sealed class ApiResponse<T>
{
    [JsonPropertyName("data")]
    public required T Data { get; init; }
}
