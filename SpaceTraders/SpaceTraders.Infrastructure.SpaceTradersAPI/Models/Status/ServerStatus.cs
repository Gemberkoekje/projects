using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Status;

public sealed class ServerStatus
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("resetDate")]
    public required string ResetDate { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("stats")]
    public StatusStats? Stats { get; init; }

    [JsonPropertyName("health")]
    public StatusHealth? Health { get; init; }

    [JsonPropertyName("leaderboards")]
    public object? Leaderboards { get; init; }

    [JsonPropertyName("serverResets")]
    public ServerResetInfo? ServerResets { get; init; }

    [JsonPropertyName("announcements")]
    public IReadOnlyList<Announcement>? Announcements { get; init; }

    [JsonPropertyName("links")]
    public IReadOnlyList<StatusLink>? Links { get; init; }
}

public sealed class StatusStats
{
    [JsonPropertyName("agents")]
    public int Agents { get; init; }

    [JsonPropertyName("ships")]
    public int Ships { get; init; }

    [JsonPropertyName("systems")]
    public int Systems { get; init; }

    [JsonPropertyName("waypoints")]
    public int Waypoints { get; init; }
}

public sealed class StatusHealth
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("lastUpdated")]
    public string? LastUpdated { get; init; }
}

public sealed class ServerResetInfo
{
    [JsonPropertyName("next")]
    public required string Next { get; init; }

    [JsonPropertyName("frequency")]
    public required string Frequency { get; init; }
}

public sealed class Announcement
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }
}

public sealed class StatusLink
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}
