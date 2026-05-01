using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Sync;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using Wolverine;

namespace SpaceTraders.API.Services;

/// <summary>Syncs agent, ships, and contracts from the SpaceTraders API on startup.</summary>
public sealed class StartupSyncService(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<StartupSyncService> logger) : IHostedService
{
    private const int ShipPageSize = 20;

    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ILogger<StartupSyncService> _logger = logger;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var apiClient = scope.ServiceProvider.GetRequiredService<ISpaceTradersApiClient>();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var now = TimeProvider.System.GetUtcNow();

        var agent = await apiClient.GetMyAgentAsync(cancellationToken);
        var existingAgent = await dbContext.Agents.FindAsync([dbContext.AgentToken, agent.Symbol], cancellationToken);
        if (existingAgent is null)
        {
            dbContext.Agents.Add(new CachedAgent
            {
                AgentToken = dbContext.AgentToken,
                Symbol = agent.Symbol,
                AccountId = agent.AccountId,
                HeadquartersSymbol = agent.Headquarters,
                StartingFaction = agent.StartingFaction,
                Credits = agent.Credits,
                ShipCount = agent.ShipCount,
                LastSyncedAt = now,
            });
        }
        else
        {
            dbContext.Entry(existingAgent).CurrentValues.SetValues(new CachedAgent
            {
                AgentToken = dbContext.AgentToken,
                Symbol = agent.Symbol,
                AccountId = agent.AccountId,
                HeadquartersSymbol = agent.Headquarters,
                StartingFaction = agent.StartingFaction,
                Credits = agent.Credits,
                ShipCount = agent.ShipCount,
                LastSyncedAt = now,
            });
        }

        var ships = await GetAllShipsAsync(apiClient, cancellationToken);
        foreach (var ship in ships)
        {
            var existingShip = await dbContext.Ships.FindAsync([dbContext.AgentToken, ship.Symbol], cancellationToken);
            if (existingShip is null)
            {
                dbContext.Ships.Add(new CachedShip
                {
                    AgentToken = dbContext.AgentToken,
                    Symbol = ship.Symbol,
                    SystemSymbol = ship.Nav?.SystemSymbol,
                    WaypointSymbol = ship.Nav?.WaypointSymbol,
                    DestWaypointSymbol = ship.Nav?.Route?.Destination?.Symbol,
                    Status = ship.Nav?.Status,
                    FlightMode = ship.Nav?.FlightMode,
                    FuelCurrent = ship.Fuel?.Current ?? 0,
                    FuelCapacity = ship.Fuel?.Capacity ?? 0,
                    CargoCurrent = ship.Cargo?.Units ?? 0,
                    CargoCapacity = ship.Cargo?.Capacity ?? 0,
                    ArrivesAt = ship.Nav?.Route?.Arrival,
                    LastSyncedAt = now,
                });
            }
            else
            {
                dbContext.Entry(existingShip).CurrentValues.SetValues(new CachedShip
                {
                    AgentToken = dbContext.AgentToken,
                    Symbol = ship.Symbol,
                    SystemSymbol = ship.Nav?.SystemSymbol,
                    WaypointSymbol = ship.Nav?.WaypointSymbol,
                    DestWaypointSymbol = ship.Nav?.Route?.Destination?.Symbol,
                    Status = ship.Nav?.Status,
                    FlightMode = ship.Nav?.FlightMode,
                    ShipType = existingShip.ShipType,
                    MountsJson = existingShip.MountsJson,
                    FuelCurrent = ship.Fuel?.Current ?? 0,
                    FuelCapacity = ship.Fuel?.Capacity ?? 0,
                    CargoCurrent = ship.Cargo?.Units ?? existingShip.CargoCurrent,
                    CargoCapacity = ship.Cargo?.Capacity ?? existingShip.CargoCapacity,
                    CargoJson = existingShip.CargoJson,
                    ArrivesAt = ship.Nav?.Route?.Arrival,
                    LastSyncedAt = now,
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Startup ship refresh saved for {Count} ship(s).", ships.Count);

        await EnsureSystemsForShipsAreCachedAsync(apiClient, dbContext, ships, now, cancellationToken);
        await EnsureFacilitiesForShipsAreCachedAsync(apiClient, dbContext, ships, now, cancellationToken);

        var contracts = await apiClient.GetMyContractsAsync(cancellationToken: cancellationToken);
        foreach (var contract in contracts.Data)
        {
            var existingContract = await dbContext.Contracts.FindAsync([dbContext.AgentToken, contract.Id], cancellationToken);
            if (existingContract is null)
            {
                dbContext.Contracts.Add(new CachedContract
                {
                    AgentToken = dbContext.AgentToken,
                    Id = contract.Id,
                    FactionSymbol = contract.FactionSymbol,
                    Type = contract.Type,
                    IsAccepted = contract.Accepted,
                    IsFulfilled = contract.Fulfilled,
                    Expiration = contract.Expiration,
                    DeadlineToAccept = contract.DeadlineToAccept,
                    LastSyncedAt = now,
                });
            }
            else
            {
                dbContext.Entry(existingContract).CurrentValues.SetValues(new CachedContract
                {
                    AgentToken = dbContext.AgentToken,
                    Id = contract.Id,
                    FactionSymbol = contract.FactionSymbol,
                    Type = contract.Type,
                    IsAccepted = contract.Accepted,
                    IsFulfilled = contract.Fulfilled,
                    Expiration = contract.Expiration,
                    DeadlineToAccept = contract.DeadlineToAccept,
                    LastSyncedAt = now,
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Completed startup sync for agent, ships, and contracts.");

        await bus.InvokeAsync(new RunPhase3OnboardingCommand(), cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<List<SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet.Ship>> GetAllShipsAsync(
        ISpaceTradersApiClient apiClient,
        CancellationToken cancellationToken)
    {
        var page = 1;
        var allShips = new List<SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet.Ship>();

        while (true)
        {
            var response = await apiClient.GetMyShipsAsync(page, ShipPageSize, cancellationToken);
            if (response.Data.Count == 0)
            {
                break;
            }

            allShips.AddRange(response.Data);

            if (allShips.Count >= response.Meta.Total)
            {
                break;
            }

            page++;
        }

        return allShips;
    }

    private static async Task EnsureSystemsForShipsAreCachedAsync(
        ISpaceTradersApiClient apiClient,
        SpaceTradersDbContext dbContext,
        IReadOnlyList<SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet.Ship> ships,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var systemSymbols = ships
            .Select(s => s.Nav?.SystemSymbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        foreach (var systemSymbol in systemSymbols)
        {
            var isSystemCached = await dbContext.Systems
                .AsNoTracking()
                .AnyAsync(s => s.AgentToken == dbContext.AgentToken && s.Symbol == systemSymbol, cancellationToken);

            var hasWaypointsCached = await dbContext.Waypoints
                .AsNoTracking()
                .AnyAsync(w => w.AgentToken == dbContext.AgentToken && w.SystemSymbol == systemSymbol, cancellationToken);

            if (!isSystemCached)
            {
                var system = await apiClient.GetSystemAsync(systemSymbol, cancellationToken);
                dbContext.Systems.Add(new CachedSystem
                {
                    AgentToken = dbContext.AgentToken,
                    Symbol = system.Symbol,
                    SectorSymbol = system.SectorSymbol,
                    Type = system.Type,
                    X = system.X,
                    Y = system.Y,
                    LastObservedAt = now,
                });
            }

            if (hasWaypointsCached)
            {
                continue;
            }

            var page = 1;
            const int limit = 20;

            while (true)
            {
                var waypoints = await apiClient.GetWaypointsAsync(systemSymbol, page, limit, cancellationToken);
                if (waypoints.Data.Count == 0)
                {
                    break;
                }

                foreach (var waypoint in waypoints.Data)
                {
                    var hasMarket = waypoint.Traits?.Any(t => t.Symbol == "MARKETPLACE") == true;
                    var hasShipyard = waypoint.Traits?.Any(t => t.Symbol == "SHIPYARD") == true;

                    dbContext.Waypoints.Add(new CachedWaypoint
                    {
                        AgentToken = dbContext.AgentToken,
                        Symbol = waypoint.Symbol,
                        SystemSymbol = waypoint.SystemSymbol,
                        Type = waypoint.Type,
                        X = waypoint.X,
                        Y = waypoint.Y,
                        HasMarket = hasMarket,
                        HasShipyard = hasShipyard,
                        LastObservedAt = now,
                    });
                }

                if (waypoints.Data.Count < limit)
                {
                    break;
                }

                page++;
            }
        }
    }

    private static async Task EnsureFacilitiesForShipsAreCachedAsync(
        ISpaceTradersApiClient apiClient,
        SpaceTradersDbContext dbContext,
        IReadOnlyList<SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet.Ship> ships,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var locatedWaypoints = ships
            .Where(s => s.Nav is not null
                && !string.Equals(s.Nav.Status, "IN_TRANSIT", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(s.Nav.SystemSymbol)
                && !string.IsNullOrWhiteSpace(s.Nav.WaypointSymbol))
            .Select(s => new
            {
                SystemSymbol = s.Nav!.SystemSymbol,
                WaypointSymbol = s.Nav.WaypointSymbol,
            })
            .DistinctBy(x => x.WaypointSymbol)
            .ToList();

        foreach (var location in locatedWaypoints)
        {
            var systemSymbol = location.SystemSymbol;
            var waypointSymbol = location.WaypointSymbol;

            var waypoint = await dbContext.Waypoints.FindAsync([dbContext.AgentToken, waypointSymbol], cancellationToken);

            if (waypoint is null)
            {
                var remoteWaypoint = await apiClient.GetWaypointAsync(systemSymbol, waypointSymbol, cancellationToken);
                var hasMarketFromApi = remoteWaypoint.Traits?.Any(t => t.Symbol == "MARKETPLACE") == true;
                var hasShipyardFromApi = remoteWaypoint.Traits?.Any(t => t.Symbol == "SHIPYARD") == true;

                waypoint = new CachedWaypoint
                {
                    AgentToken = dbContext.AgentToken,
                    Symbol = remoteWaypoint.Symbol,
                    SystemSymbol = remoteWaypoint.SystemSymbol,
                    Type = remoteWaypoint.Type,
                    X = remoteWaypoint.X,
                    Y = remoteWaypoint.Y,
                    HasMarket = hasMarketFromApi,
                    HasShipyard = hasShipyardFromApi,
                    LastObservedAt = now,
                };

                dbContext.Waypoints.Add(waypoint);
            }

            if (waypoint.HasMarket)
            {
                var market = await apiClient.GetMarketAsync(systemSymbol, waypointSymbol, cancellationToken);
                var cachedMarket = await dbContext.Markets.FindAsync([dbContext.AgentToken, waypointSymbol], cancellationToken);
                var marketValues = new CachedMarket
                {
                    AgentToken = dbContext.AgentToken,
                    WaypointSymbol = waypointSymbol,
                    SystemSymbol = systemSymbol,
                    TradeGoodsJson = market.TradeGoods is not null ? JsonSerializer.Serialize(market.TradeGoods) : null,
                    ImportsJson = market.Imports is not null ? JsonSerializer.Serialize(market.Imports) : null,
                    ExportsJson = market.Exports is not null ? JsonSerializer.Serialize(market.Exports) : null,
                    ExchangeJson = market.Exchange is not null ? JsonSerializer.Serialize(market.Exchange) : null,
                    LastObservedAt = now,
                };

                if (cachedMarket is null)
                {
                    dbContext.Markets.Add(marketValues);
                }
                else
                {
                    dbContext.Entry(cachedMarket).CurrentValues.SetValues(marketValues);
                }
            }

            if (waypoint.HasShipyard)
            {
                var shipyard = await apiClient.GetShipyardAsync(systemSymbol, waypointSymbol, cancellationToken);
                var cachedShipyard = await dbContext.Shipyards.FindAsync([dbContext.AgentToken, waypointSymbol], cancellationToken);
                var shipyardValues = new CachedShipyard
                {
                    AgentToken = dbContext.AgentToken,
                    WaypointSymbol = waypointSymbol,
                    SystemSymbol = systemSymbol,
                    ShipTypesJson = shipyard.Ships is not null ? JsonSerializer.Serialize(shipyard.Ships) : null,
                    LastObservedAt = now,
                };

                if (cachedShipyard is null)
                {
                    dbContext.Shipyards.Add(shipyardValues);
                }
                else
                {
                    dbContext.Entry(cachedShipyard).CurrentValues.SetValues(shipyardValues);
                }
            }
        }
    }
}
