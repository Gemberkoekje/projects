namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Metrics;

/// <summary>
/// Turns a request path into its route template, so the metrics count by endpoint rather than by
/// ship, waypoint or page: <c>/v2/my/ships/AGENT-1/navigate</c> becomes
/// <c>my/ships/{shipSymbol}/navigate</c>, and <c>/v2/</c> (the server status) becomes <c>/</c>.
/// </summary>
public static class ApiEndpointTemplate
{
    /// <summary>The path segments that name a collection, and the parameter that follows each.</summary>
    private static readonly Dictionary<string, string> Parameters = new(StringComparer.Ordinal)
    {
        ["agents"] = "{agentSymbol}",
        ["contracts"] = "{contractId}",
        ["factions"] = "{factionSymbol}",
        ["ships"] = "{shipSymbol}",
        ["systems"] = "{systemSymbol}",
        ["waypoints"] = "{waypointSymbol}",
    };

    /// <summary>The route template of <paramref name="path"/>, without the API version and query string.</summary>
    public static string FromPath(string path)
    {
        var withoutQuery = path.Split('?', 2)[0];
        var segments = withoutQuery.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 0 && IsVersion(segments[0]))
        {
            segments.RemoveAt(0);
        }

        for (var index = 1; index < segments.Count; index++)
        {
            if (Parameters.TryGetValue(segments[index - 1], out var parameter))
            {
                segments[index] = parameter;
            }
        }

        return segments.Count == 0 ? "/" : string.Join('/', segments);
    }

    private static bool IsVersion(string segment)
        => segment.Length > 1 && segment[0] == 'v' && segment.Skip(1).All(char.IsAsciiDigit);
}
