using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Automation;

public interface IMarketplaceRoutePlanner
{
    Task<IReadOnlyList<string>> BuildRouteAsync(
        string startWaypointSymbol,
        IReadOnlyList<WaypointCacheModel> candidateMarketplaces,
        CancellationToken cancellationToken = default);
}

public sealed class MarketplaceRoutePlanner : IMarketplaceRoutePlanner
{
    public Task<IReadOnlyList<string>> BuildRouteAsync(
        string startWaypointSymbol,
        IReadOnlyList<WaypointCacheModel> candidateMarketplaces,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startWaypointSymbol);

        if (candidateMarketplaces.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var unvisited = candidateMarketplaces
            .Where(waypoint => !string.IsNullOrWhiteSpace(waypoint.Symbol))
            .GroupBy(waypoint => waypoint.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (unvisited.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var route = new List<string>(unvisited.Count);

        var current = unvisited
            .FirstOrDefault(waypoint => string.Equals(waypoint.Symbol, startWaypointSymbol, StringComparison.OrdinalIgnoreCase))
            ?? unvisited
                .OrderBy(waypoint => waypoint.Symbol, StringComparer.OrdinalIgnoreCase)
                .First();

        route.Add(current.Symbol);
        unvisited.Remove(current);

        while (unvisited.Count > 0)
        {
            var next = unvisited
                .OrderBy(waypoint => DistanceSquared(current, waypoint))
                .ThenBy(waypoint => waypoint.Symbol, StringComparer.OrdinalIgnoreCase)
                .First();

            route.Add(next.Symbol);
            unvisited.Remove(next);
            current = next;
        }

        return Task.FromResult<IReadOnlyList<string>>(route);
    }

    private static long DistanceSquared(WaypointCacheModel from, WaypointCacheModel to)
    {
        var dx = (long)to.X - from.X;
        var dy = (long)to.Y - from.Y;
        return (dx * dx) + (dy * dy);
    }
}
