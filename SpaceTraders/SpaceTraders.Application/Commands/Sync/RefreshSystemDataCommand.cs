using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Sync;

public sealed record RefreshSystemDataCommand
{
    public required string SystemSymbol { get; init; }

    public bool ForceRefresh { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RefreshSystemDataCommand(string SystemSymbol, bool ForceRefresh = false)
    {
        this.SystemSymbol = SystemSymbol;
        this.ForceRefresh = ForceRefresh;
    }
}

public sealed class RefreshSystemDataHandler(
    ISpaceTradersPort port,
    ISystemRepository systems,
    IWaypointRepository waypoints,
    ILogger<RefreshSystemDataHandler> logger)
{
    private static readonly TimeSpan SystemTtl = TimeSpan.FromDays(7);

    public async Task Handle(RefreshSystemDataCommand command, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();

        if (!command.ForceRefresh)
        {
            var cachedSystem = await systems.FindAsync(command.SystemSymbol, cancellationToken);
            var cachedWaypoints = await waypoints.GetBySystemAsync(command.SystemSymbol, cancellationToken);

            if (cachedSystem is not null && cachedSystem.LastObservedAt + SystemTtl > now && cachedWaypoints.Count > 0)
            {
                logger.LogDebug("Skipping system refresh for {System}: cache is fresh.", command.SystemSymbol);
                return;
            }
        }

        var system = await port.GetSystemAsync(command.SystemSymbol, cancellationToken);
        await systems.UpsertAsync(new SystemCacheModel(
            system.Symbol,
            system.SectorSymbol,
            system.Type,
            system.X,
            system.Y,
            now), cancellationToken);

        var waypointModels = new List<WaypointCacheModel>();
        var page = 1;
        const int limit = 20;

        while (true)
        {
            var pageResult = await port.GetWaypointsAsync(command.SystemSymbol, page, limit, cancellationToken);
            if (pageResult.Items.Count == 0)
            {
                break;
            }

            waypointModels.AddRange(pageResult.Items.Select(waypoint => new WaypointCacheModel(
                waypoint.Symbol,
                waypoint.SystemSymbol,
                waypoint.Type,
                waypoint.X,
                waypoint.Y,
                waypoint.HasMarket,
                waypoint.HasShipyard,
                now)));

            if (waypointModels.Count >= pageResult.Total)
            {
                break;
            }

            page++;
        }

        if (waypointModels.Count > 0)
        {
            await waypoints.UpsertRangeAsync(waypointModels, cancellationToken);
        }

        logger.LogInformation(
            "System data refreshed for {System} with {WaypointCount} waypoint(s).",
            command.SystemSymbol,
            waypointModels.Count);
    }
}
