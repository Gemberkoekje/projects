using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Markets;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Shipyards;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems;

namespace SpaceTraders.API.Services;

/// <summary>
/// Saves a comprehensive JSON snapshot of the agent's initial state to the database on startup.
/// The snapshot includes agent credits, all owned ships with full component/mount/module stats,
/// all waypoints in each system the ships are located in, and detailed market and shipyard data
/// (including per-ship specs with modules and mounts) for every applicable waypoint.
/// </summary>
public sealed class StartupSnapshotService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<StartupSnapshotService> logger) : IHostedService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TakeSnapshotAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StartupSnapshotService failed; snapshot will not be saved this run.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task TakeSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<ISpaceTradersApiClient>();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        var isInitial = !await dbContext.StartupSnapshots
            .AsNoTracking()
            .AnyAsync(s => s.AgentToken == dbContext.AgentToken, cancellationToken);

        var agent = await apiClient.GetMyAgentAsync(cancellationToken);
        var ships = await FetchAllShipsAsync(apiClient, cancellationToken);

        var systemSymbols = ships
            .Select(s => s.Nav?.SystemSymbol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        var systemSnapshots = new List<SystemSnapshotData>();
        foreach (var systemSymbol in systemSymbols)
        {
            var systemInfo = await apiClient.GetSystemAsync(systemSymbol, cancellationToken);
            var waypoints = await FetchAllWaypointsAsync(apiClient, systemSymbol, cancellationToken);

            var waypointSnapshots = new List<WaypointSnapshotData>();
            foreach (var waypoint in waypoints)
            {
                var hasMarket = waypoint.Traits?.Any(t => t.Symbol == "MARKETPLACE") == true;
                var hasShipyard = waypoint.Traits?.Any(t => t.Symbol == "SHIPYARD") == true;

                Market? market = null;
                Shipyard? shipyard = null;

                if (hasMarket)
                {
                    try
                    {
                        market = await apiClient.GetMarketAsync(systemSymbol, waypoint.Symbol, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not fetch market data for waypoint {Waypoint}.", waypoint.Symbol);
                    }
                }

                if (hasShipyard)
                {
                    try
                    {
                        shipyard = await apiClient.GetShipyardAsync(systemSymbol, waypoint.Symbol, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not fetch shipyard data for waypoint {Waypoint}.", waypoint.Symbol);
                    }
                }

                waypointSnapshots.Add(new WaypointSnapshotData(waypoint, market, shipyard));
            }

            systemSnapshots.Add(new SystemSnapshotData(systemInfo, waypointSnapshots));
        }

        var snapshot = new GameStateSnapshotData(
            CapturedAt: TimeProvider.System.GetUtcNow(),
            Agent: agent,
            Ships: ships,
            Systems: systemSnapshots);

        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);

        dbContext.StartupSnapshots.Add(new StartupSnapshot
        {
            AgentToken = dbContext.AgentToken,
            SnapshotJson = json,
            CapturedAt = snapshot.CapturedAt,
            IsInitialSnapshot = isInitial,
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Startup snapshot saved (initial: {IsInitial}, ships: {ShipCount}, systems: {SystemCount}).",
            isInitial,
            ships.Count,
            systemSnapshots.Count);
    }

    private static async Task<List<Infrastructure.SpaceTradersAPI.Models.Fleet.Ship>> FetchAllShipsAsync(
        ISpaceTradersApiClient apiClient,
        CancellationToken cancellationToken)
    {
        var all = new List<Infrastructure.SpaceTradersAPI.Models.Fleet.Ship>();
        var page = 1;
        const int limit = 20;

        while (true)
        {
            var response = await apiClient.GetMyShipsAsync(page, limit, cancellationToken);
            all.AddRange(response.Data);

            if (all.Count >= response.Meta.Total || response.Data.Count == 0)
            {
                break;
            }

            page++;
        }

        return all;
    }

    private static async Task<List<Waypoint>> FetchAllWaypointsAsync(
        ISpaceTradersApiClient apiClient,
        string systemSymbol,
        CancellationToken cancellationToken)
    {
        var all = new List<Waypoint>();
        var page = 1;
        const int limit = 20;

        while (true)
        {
            var response = await apiClient.GetWaypointsAsync(systemSymbol, page, limit, cancellationToken);
            all.AddRange(response.Data);

            if (all.Count >= response.Meta.Total || response.Data.Count == 0)
            {
                break;
            }

            page++;
        }

        return all;
    }

    private sealed record GameStateSnapshotData(
        DateTimeOffset CapturedAt,
        Agent Agent,
        IReadOnlyList<Infrastructure.SpaceTradersAPI.Models.Fleet.Ship> Ships,
        IReadOnlyList<SystemSnapshotData> Systems);

    private sealed record SystemSnapshotData(
        SystemInfo System,
        IReadOnlyList<WaypointSnapshotData> Waypoints);

    private sealed record WaypointSnapshotData(
        Waypoint Waypoint,
        Market? Market,
        Shipyard? Shipyard);
}
