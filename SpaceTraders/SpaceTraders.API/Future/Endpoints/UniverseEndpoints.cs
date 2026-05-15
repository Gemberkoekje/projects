using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps the <c>/universe</c> read-only API endpoints.</summary>
public static class UniverseEndpoints
{
    private const string JumpGateKeyPrefix = "Navigation.JumpGateConnections.";

    /// <summary>Registers the universe route group on the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapUniverseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/universe");

        group.MapGet("/systems", async (
            ISystemRepository systemRepo,
            IWaypointRepository waypointRepo,
            CancellationToken ct) =>
        {
            var systems = await systemRepo.GetAllAsync(ct);
            var visitedSet = (await waypointRepo.GetVisitedSystemSymbolsAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var result = systems.Select(s => new
            {
                s.Symbol,
                s.SectorSymbol,
                s.Type,
                s.X,
                s.Y,
                s.LastObservedAt,
                IsVisited = visitedSet.Contains(s.Symbol),
            });

            return Results.Ok(result);
        });

        group.MapGet("/jump-connections", async (ISettingsRepository settings, CancellationToken ct) =>
        {
            var entries = await settings.GetByKeyPrefixAsync(JumpGateKeyPrefix, ct);
            var connections = new List<object>();

            foreach (var (key, value) in entries)
            {
                var waypointSymbol = key[JumpGateKeyPrefix.Length..];
                var fromSystem = SystemFromWaypoint(waypointSymbol);
                if (string.IsNullOrEmpty(fromSystem)) continue;

                var entry = TryParseEntry(value);
                if (entry is null) continue;

                foreach (var toSystem in entry.ConnectedSystems)
                {
                    connections.Add(new { FromSystem = fromSystem, ToSystem = toSystem });
                }
            }

            return Results.Ok(connections);
        });

        group.MapGet("/systems/{symbol}/map", async (
            string symbol,
            ISystemRepository systemRepo,
            IWaypointRepository waypointRepo,
            CancellationToken ct) =>
        {
            var system = await systemRepo.FindAsync(symbol, ct);
            if (system is null)
            {
                return Results.NotFound();
            }

            var waypoints = await waypointRepo.GetBySystemAsync(symbol, ct);
            return Results.Ok(new { System = system, Waypoints = waypoints });
        });

        return app;
    }

    private static string SystemFromWaypoint(string waypointSymbol)
    {
        // Waypoint symbols are in the form SECTOR-SYSTEM-ID (e.g. X1-SN-12345).
        // The system symbol is everything up to the second hyphen.
        var firstHyphen = waypointSymbol.IndexOf('-');
        if (firstHyphen < 0) return string.Empty;
        var secondHyphen = waypointSymbol.IndexOf('-', firstHyphen + 1);
        return secondHyphen >= 0 ? waypointSymbol[..secondHyphen] : waypointSymbol;
    }

    private sealed record JumpGateEntry(string WaypointSymbol, IReadOnlyList<string> ConnectedSystems);

    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static JumpGateEntry? TryParseEntry(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<JumpGateEntry>(json, _jsonOptions);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
