using System.Text.Json.Serialization;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Status;

public sealed class ServerStatus
{
    [JsonPropertyName("status")]
    required public string Status { get; init; }

    [JsonPropertyName("version")]
    required public string Version { get; init; }

    [JsonPropertyName("resetDate")]
    required public string ResetDate { get; init; }

    [JsonPropertyName("description")]
    required public string Description { get; init; }

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
    required public string Status { get; init; }

    [JsonPropertyName("lastUpdated")]
    public string? LastUpdated { get; init; }
}

public sealed class ServerResetInfo
{
    [JsonPropertyName("next")]
    required public string Next { get; init; }

    [JsonPropertyName("frequency")]
    required public string Frequency { get; init; }
}

public sealed class Announcement
{
    [JsonPropertyName("title")]
    required public string Title { get; init; }

    [JsonPropertyName("body")]
    required public string Body { get; init; }
}

public sealed class StatusLink
{
    [JsonPropertyName("name")]
    required public string Name { get; init; }

    [JsonPropertyName("url")]
    required public string Url { get; init; }
}
