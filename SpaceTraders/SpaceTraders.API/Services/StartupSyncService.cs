using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Domain.Enums;
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
        var now = TimeProvider.System.GetUtcNow();

        var agent = await apiClient.GetMyAgentAsync(cancellationToken);
        var existingAgent = await dbContext.Agents.FindAsync([dbContext.AgentId, agent.Symbol], cancellationToken);
        if (existingAgent is null)
        {
            dbContext.Agents.Add(new CachedAgent
            {
                AgentId = dbContext.AgentId,
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
                AgentId = dbContext.AgentId,
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
            var existingShip = await dbContext.Ships.FindAsync([dbContext.AgentId, ship.Symbol], cancellationToken);
            var shipType = ship.Registration?.Role ?? string.Empty;
            var mountsJson = ship.Mounts is null
                ? null
                : JsonSerializer.Serialize(ship.Mounts.Select(m => m.Symbol).ToList());
            var modulesJson = ship.Modules is null ? null : JsonSerializer.Serialize(ship.Modules);
            var frameJson = ship.Frame is null ? null : JsonSerializer.Serialize(ship.Frame);
            var reactorJson = ship.Reactor is null ? null : JsonSerializer.Serialize(ship.Reactor);
            var engineJson = ship.Engine is null ? null : JsonSerializer.Serialize(ship.Engine);
            var cooldownExpiresAt = ship.Cooldown?.Expiration;
            var cargoJson = ship.Cargo?.Inventory is null
                ? existingShip?.CargoJson
                : JsonSerializer.Serialize(ship.Cargo.Inventory.Select(i => new { i.Symbol, i.Units }).ToList());

            var cachedShip = existingShip ?? new CachedShip { AgentId = dbContext.AgentId, Symbol = ship.Symbol };
            if (existingShip is null)
            {
                dbContext.Ships.Add(cachedShip);
            }

            // Game state only. The goal columns belong to the bot, and a restart must not clear them.
            cachedShip.SystemSymbol = ship.Nav?.SystemSymbol;
            cachedShip.WaypointSymbol = ship.Nav?.WaypointSymbol;
            cachedShip.DestWaypointSymbol = ship.Nav?.Route?.Destination?.Symbol;
            cachedShip.Status = ship.Nav?.Status;
            cachedShip.LocalStatus = ShipLocalStatusMapper.FromApiStatus(ship.Nav?.Status);
            cachedShip.FlightMode = ship.Nav?.FlightMode;
            cachedShip.ShipType = shipType;
            cachedShip.MountsJson = mountsJson;
            cachedShip.ModulesJson = modulesJson;
            cachedShip.FrameJson = frameJson;
            cachedShip.ReactorJson = reactorJson;
            cachedShip.EngineJson = engineJson;
            cachedShip.CooldownExpiresAt = cooldownExpiresAt;
            cachedShip.FuelCurrent = ship.Fuel?.Current ?? 0;
            cachedShip.FuelCapacity = ship.Fuel?.Capacity ?? 0;
            cachedShip.CargoCurrent = ship.Cargo?.Units ?? cachedShip.CargoCurrent;
            cachedShip.CargoCapacity = ship.Cargo?.Capacity ?? cachedShip.CargoCapacity;
            cachedShip.CargoJson = cargoJson;
            cachedShip.ArrivesAt = ship.Nav?.Route?.Arrival;
            cachedShip.LastSyncedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Startup ship refresh saved for {Count} ship(s).", ships.Count);

        await EnsureSystemsForShipsAreCachedAsync(apiClient, dbContext, ships, now, cancellationToken);
        await EnsureFacilitiesForShipsAreCachedAsync(apiClient, dbContext, ships, now, cancellationToken);

        var contracts = await apiClient.GetMyContractsAsync(cancellationToken: cancellationToken);
        foreach (var contract in contracts.Data)
        {
            var existingContract = await dbContext.Contracts.FindAsync([dbContext.AgentId, contract.Id], cancellationToken);

            // With its terms, as every other path stores a contract: the contract plan works from the
            // deliverables, and without them a restart blanked them until the next delivery (B31).
            var contractValues = new CachedContract
            {
                AgentId = dbContext.AgentId,
                Id = contract.Id,
                FactionSymbol = contract.FactionSymbol,
                Type = contract.Type,
                IsAccepted = contract.Accepted,
                IsFulfilled = contract.Fulfilled,
                Expiration = contract.Expiration,
                DeadlineToAccept = contract.DeadlineToAccept,
                TermsDeadline = contract.Terms?.Deadline,
                DeliverablesJson = contract.Terms?.Deliver is { } deliver
                    ? JsonSerializer.Serialize(deliver.Select(d => new ContractDeliverableDto(d.TradeSymbol, d.DestinationSymbol, d.UnitsRequired, d.UnitsFulfilled)).ToList())
                    : null,
                LastSyncedAt = now,
            };

            if (existingContract is null)
            {
                dbContext.Contracts.Add(contractValues);
            }
            else
            {
                dbContext.Entry(existingContract).CurrentValues.SetValues(contractValues);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Completed startup sync for agent, ships, and contracts.");
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
                .AnyAsync(s => s.AgentId == dbContext.AgentId && s.Symbol == systemSymbol, cancellationToken);

            var cachedWaypoints = await dbContext.Waypoints
                .Where(w => w.AgentId == dbContext.AgentId && w.SystemSymbol == systemSymbol)
                .ToDictionaryAsync(w => w.Symbol, StringComparer.OrdinalIgnoreCase, cancellationToken);

            if (!isSystemCached)
            {
                var system = await apiClient.GetSystemAsync(systemSymbol, cancellationToken);
                dbContext.Systems.Add(new CachedSystem
                {
                    AgentId = dbContext.AgentId,
                    Symbol = system.Symbol,
                    SectorSymbol = system.SectorSymbol,
                    Type = system.Type,
                    X = system.X,
                    Y = system.Y,
                    LastObservedAt = now,
                });
            }

            // Waypoints cached before sync stored traits have none (B34): the system is fetched again
            // for them, once; a waypoint without traits is stored with an empty list.
            if (cachedWaypoints.Count > 0 && cachedWaypoints.Values.All(w => w.TraitsJson is not null))
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
                    if (cachedWaypoints.TryGetValue(waypoint.Symbol, out var cached))
                    {
                        // When it was last observed is the scout plan's, so it stays.
                        dbContext.Entry(cached).CurrentValues.SetValues(ToCachedWaypoint(dbContext.AgentId, waypoint, cached.LastObservedAt));
                    }
                    else
                    {
                        dbContext.Waypoints.Add(ToCachedWaypoint(dbContext.AgentId, waypoint, now));
                    }
                }

                if (waypoints.Data.Count < limit)
                {
                    break;
                }

                page++;
            }
        }
    }

    /// <summary>
    /// A waypoint as the cache stores it: with its traits and modifiers, which tell what an asteroid
    /// yields (B34), in the shape <c>SpaceTradersPortAdapter</c> writes them (<c>[{"symbol":…}]</c>).
    /// </summary>
    private static CachedWaypoint ToCachedWaypoint(
        string agentId,
        SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems.Waypoint waypoint,
        DateTimeOffset lastObservedAt)
        => new()
        {
            AgentId = agentId,
            Symbol = waypoint.Symbol,
            SystemSymbol = waypoint.SystemSymbol,
            Type = waypoint.Type,
            X = waypoint.X,
            Y = waypoint.Y,
            HasMarket = waypoint.Traits?.Any(t => t.Symbol == "MARKETPLACE") == true,
            HasShipyard = waypoint.Traits?.Any(t => t.Symbol == "SHIPYARD") == true,
            TraitsJson = JsonSerializer.Serialize(waypoint.Traits ?? []),
            ModifiersJson = JsonSerializer.Serialize(waypoint.Modifiers ?? []),
            OrbitalsJson = waypoint.Orbitals is null ? null : JsonSerializer.Serialize(waypoint.Orbitals),
            ParentSymbol = waypoint.Orbits,
            IsUnderConstruction = waypoint.IsUnderConstruction,
            ChartJson = waypoint.Chart is null ? null : JsonSerializer.Serialize(waypoint.Chart),
            LastObservedAt = lastObservedAt,
        };

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

            var waypoint = await dbContext.Waypoints.FindAsync([dbContext.AgentId, waypointSymbol], cancellationToken);

            if (waypoint is null)
            {
                var remoteWaypoint = await apiClient.GetWaypointAsync(systemSymbol, waypointSymbol, cancellationToken);
                waypoint = ToCachedWaypoint(dbContext.AgentId, remoteWaypoint, now);
                dbContext.Waypoints.Add(waypoint);
            }

            if (waypoint.HasMarket)
            {
                var market = await apiClient.GetMarketAsync(systemSymbol, waypointSymbol, cancellationToken);
                var cachedMarket = await dbContext.Markets.FindAsync([dbContext.AgentId, waypointSymbol], cancellationToken);
                var marketValues = new CachedMarket
                {
                    AgentId = dbContext.AgentId,
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
                else if (market.TradeGoods is not null)
                {
                    // An answer without prices would wipe the cached ones, so it is left out, as the market watch does (B62).
                    dbContext.Entry(cachedMarket).CurrentValues.SetValues(marketValues);
                }
            }

            if (waypoint.HasShipyard)
            {
                var shipyard = await apiClient.GetShipyardAsync(systemSymbol, waypointSymbol, cancellationToken);
                var cachedShipyard = await dbContext.Shipyards.FindAsync([dbContext.AgentId, waypointSymbol], cancellationToken);
                // The same columns an arrival writes (SpaceTradersPortAdapter): purchases read the
                // prices from the ships.
                var shipyardValues = new CachedShipyard
                {
                    AgentId = dbContext.AgentId,
                    WaypointSymbol = waypointSymbol,
                    SystemSymbol = systemSymbol,
                    ShipTypesJson = shipyard.ShipTypes is not null ? JsonSerializer.Serialize(shipyard.ShipTypes) : null,
                    ShipsDetailJson = shipyard.Ships is not null ? JsonSerializer.Serialize(shipyard.Ships) : null,
                    LastObservedAt = now,
                };

                if (cachedShipyard is null)
                {
                    dbContext.Shipyards.Add(shipyardValues);
                }
                else if (shipyard.Ships is not null || cachedShipyard.ShipsDetailJson is null)
                {
                    // An answer without the ships for sale would wipe the cached ones, and the prices a purchase reads, so
                    // it is left out, as the repository does (B64).
                    dbContext.Entry(cachedShipyard).CurrentValues.SetValues(shipyardValues);
                }
            }
        }
    }
}
