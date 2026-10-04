using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.API.Services;

/// <summary>
/// Saves a JSON snapshot of the game state as the bot has cached it: the agent, its ships (with their goals), its
/// contracts, and every system it has a ship, market or shipyard in, with the system's waypoints and every market and
/// shipyard the cache holds there, as last seen (each with when). It calls no API itself (B35).
/// </summary>
/// <remarks>
/// A snapshot is taken at every start (<see cref="StartupSnapshotService"/>), and whenever the cached shipyards or markets
/// list a ship type or good no snapshot of the run held yet (<see cref="DiscoverySnapshotService"/>; slice 2.15, D73).
/// Until then a snapshot held only the market and shipyard where a ship was. One process serves one agent (a server reset
/// ends it), so what the snapshots held is kept in memory. Snapshots are taken one at a time: the startup chain takes its
/// own before it starts the discovery service.
/// </remarks>
public sealed class GameStateSnapshots(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<GameStateSnapshots> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _knownLock = new();
    private KnownTypes? _known;

    /// <summary>The ship types and goods the snapshots of this process held; null before the first.</summary>
    public KnownTypes? Known
    {
        get
        {
            lock (_knownLock)
            {
                return _known;
            }
        }
    }

    /// <summary>
    /// Counts <paramref name="types"/> as held without a snapshot, so a discovery is only what the cache lists after it:
    /// for when the startup snapshot failed.
    /// </summary>
    /// <param name="types">The ship types and goods the cache lists.</param>
    public void Remember(KnownTypes types)
    {
        lock (_knownLock)
        {
            _known = _known?.With(types) ?? types;
        }
    }

    /// <summary>The ship types and goods the cached shipyards and markets list now.</summary>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>Each ship type and good, with the waypoints that list it.</returns>
    public async Task<KnownTypes> ReadKnownAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        var shipyards = await dbContext.Shipyards.AsNoTracking().ToListAsync(cancellationToken);
        var markets = await dbContext.Markets.AsNoTracking().ToListAsync(cancellationToken);
        return KnownTypes.Of(shipyards, markets);
    }

    /// <summary>
    /// Takes a snapshot and saves it. A <see cref="StartupSnapshot.DiscoveryReason"/> snapshot lists what no snapshot of the
    /// run held yet, and is only saved when there is something.
    /// </summary>
    /// <param name="reason"><see cref="StartupSnapshot.StartupReason"/> or <see cref="StartupSnapshot.DiscoveryReason"/>.</param>
    /// <param name="cancellationToken">Stops the snapshot.</param>
    /// <returns>The snapshot saved; null when a discovery snapshot found nothing new.</returns>
    public async Task<StartupSnapshot?> TakeAsync(string reason, CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        var shipyards = await dbContext.Shipyards.AsNoTracking().ToListAsync(cancellationToken);
        var markets = await dbContext.Markets.AsNoTracking().ToListAsync(cancellationToken);
        var types = KnownTypes.Of(shipyards, markets);

        var isDiscovery = string.Equals(reason, StartupSnapshot.DiscoveryReason, StringComparison.Ordinal);
        var known = Known;
        IReadOnlyList<Discovery> discoveries = isDiscovery && known is not null ? types.NotIn(known) : [];
        if (isDiscovery && discoveries.Count == 0)
        {
            Remember(types);
            return null;
        }

        var isInitial = !await dbContext.StartupSnapshots
            .AsNoTracking()
            .AnyAsync(s => s.AgentId == dbContext.AgentId, cancellationToken);

        var agent = await dbContext.Agents.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var ships = await dbContext.Ships.AsNoTracking().OrderBy(s => s.Symbol).ToListAsync(cancellationToken);
        var contracts = await dbContext.Contracts.AsNoTracking().OrderBy(c => c.Id).ToListAsync(cancellationToken);

        // The systems the ships are in first, then every other system the cache holds a market or shipyard in.
        var systemSymbols = ships
            .Select(s => s.SystemSymbol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .Concat(markets.Select(m => m.SystemSymbol).Concat(shipyards.Select(s => s.SystemSymbol)).Order(StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var systems = await dbContext.Systems.AsNoTracking()
            .Where(s => systemSymbols.Contains(s.Symbol))
            .ToDictionaryAsync(s => s.Symbol, StringComparer.Ordinal, cancellationToken);
        var waypoints = await dbContext.Waypoints.AsNoTracking()
            .Where(w => systemSymbols.Contains(w.SystemSymbol))
            .OrderBy(w => w.Symbol)
            .ToListAsync(cancellationToken);
        var marketsAt = markets.ToDictionary(m => m.WaypointSymbol, StringComparer.Ordinal);
        var shipyardsAt = shipyards.ToDictionary(s => s.WaypointSymbol, StringComparer.Ordinal);

        var snapshot = new GameStateSnapshotData(
            CapturedAt: TimeProvider.System.GetUtcNow(),
            Reason: reason,
            Discoveries: discoveries.Count == 0 ? null : discoveries,
            Agent: agent is null ? null : ToSnapshot(agent),
            Ships: [.. ships.Select(ToSnapshot)],
            Contracts: [.. contracts.Select(ToSnapshot)],
            Systems: [.. systemSymbols.Select(symbol => new SystemSnapshotData(
                systems.TryGetValue(symbol, out var system) ? ToSnapshot(system) : null,
                [.. waypoints
                    .Where(w => w.SystemSymbol == symbol)
                    .Select(w => new WaypointSnapshotData(
                        ToSnapshot(w),
                        marketsAt.TryGetValue(w.Symbol, out var market) ? ToSnapshot(market) : null,
                        shipyardsAt.TryGetValue(w.Symbol, out var shipyard) ? ToSnapshot(shipyard) : null))]))]);

        var saved = new StartupSnapshot
        {
            AgentId = dbContext.AgentId,
            SnapshotJson = JsonSerializer.Serialize(snapshot, SerializerOptions),
            CapturedAt = snapshot.CapturedAt,
            IsInitialSnapshot = isInitial,
            Reason = reason,
            Discovered = discoveries.Count == 0 ? null : KnownTypes.Describe(discoveries),
        };
        dbContext.StartupSnapshots.Add(saved);
        await dbContext.SaveChangesAsync(cancellationToken);
        Remember(types);

        if (isDiscovery)
        {
            logger.LogInformation(
                "{EventKind:l}: {Discovered:l} Snapshot {SnapshotId} saved.",
                JournalEvents.Discovered,
                saved.Discovered,
                saved.Id);
        }
        else
        {
            logger.LogInformation(
                "Startup snapshot saved (initial: {IsInitial}, ships: {ShipCount}, systems: {SystemCount}, markets: {MarketCount}, shipyards: {ShipyardCount}).",
                isInitial,
                ships.Count,
                snapshot.Systems.Count,
                markets.Count,
                shipyards.Count);
        }

        return saved;
    }

    private static AgentSnapshotData ToSnapshot(CachedAgent agent) => new(
        agent.Symbol,
        agent.AccountId,
        agent.HeadquartersSymbol,
        agent.StartingFaction,
        agent.Credits,
        agent.ShipCount,
        agent.LastSyncedAt);

    private static ShipSnapshotData ToSnapshot(CachedShip ship) => new(
        ship.Symbol,
        ship.ShipType,
        ship.SystemSymbol,
        ship.WaypointSymbol,
        ship.DestWaypointSymbol,
        ship.Status,
        ship.FlightMode,
        ship.ArrivesAt,
        ship.CooldownExpiresAt,
        ship.FuelCurrent,
        ship.FuelCapacity,
        ship.CargoCurrent,
        ship.CargoCapacity,
        ParseJson(ship.CargoJson),
        ParseJson(ship.MountsJson),
        ParseJson(ship.ModulesJson),
        ParseJson(ship.FrameJson),
        ParseJson(ship.ReactorJson),
        ParseJson(ship.EngineJson),
        ship.GoalKind is null ? null : new GoalSnapshotData(ship.GoalId ?? Guid.Empty, ship.GoalKind, ship.GoalStatus, ParseJson(ship.GoalPayloadJson)),
        ship.LastSyncedAt);

    private static ContractSnapshotData ToSnapshot(CachedContract contract) => new(
        contract.Id,
        contract.FactionSymbol,
        contract.Type,
        contract.IsAccepted,
        contract.IsFulfilled,
        contract.Expiration,
        contract.DeadlineToAccept,
        contract.TermsDeadline,
        ParseJson(contract.DeliverablesJson),
        contract.LastSyncedAt);

    private static SystemInfoSnapshotData ToSnapshot(CachedSystem system) => new(
        system.Symbol,
        system.SectorSymbol,
        system.Type,
        system.X,
        system.Y);

    private static WaypointInfoSnapshotData ToSnapshot(CachedWaypoint waypoint) => new(
        waypoint.Symbol,
        waypoint.Type,
        waypoint.X,
        waypoint.Y,
        waypoint.HasMarket,
        waypoint.HasShipyard,
        ParseJson(waypoint.TraitsJson),
        waypoint.LastObservedAt);

    private static MarketSnapshotData ToSnapshot(CachedMarket market) => new(
        ParseJson(market.ImportsJson),
        ParseJson(market.ExportsJson),
        ParseJson(market.ExchangeJson),
        ParseJson(market.TradeGoodsJson),
        market.LastObservedAt);

    private static ShipyardSnapshotData ToSnapshot(CachedShipyard shipyard) => new(
        ParseJson(shipyard.ShipTypesJson),
        ParseJson(shipyard.ShipsDetailJson),
        shipyard.LastObservedAt);

    /// <summary>
    /// The cache keeps nested data as JSON text; the snapshot nests it as JSON. Text that doesn't
    /// parse is kept as a string, so one bad column can't cost the whole snapshot.
    /// </summary>
    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(json);
        }
    }

    private sealed record GameStateSnapshotData(
        DateTimeOffset CapturedAt,
        string Reason,
        IReadOnlyList<Discovery>? Discoveries,
        AgentSnapshotData? Agent,
        IReadOnlyList<ShipSnapshotData> Ships,
        IReadOnlyList<ContractSnapshotData> Contracts,
        IReadOnlyList<SystemSnapshotData> Systems);

    private sealed record AgentSnapshotData(
        string Symbol,
        string? AccountId,
        string? Headquarters,
        string StartingFaction,
        long Credits,
        int ShipCount,
        DateTimeOffset LastSyncedAt);

    private sealed record ShipSnapshotData(
        string Symbol,
        string ShipType,
        string? SystemSymbol,
        string? WaypointSymbol,
        string? DestinationSymbol,
        string? Status,
        string? FlightMode,
        DateTimeOffset? ArrivesAt,
        DateTimeOffset? CooldownExpiresAt,
        int FuelCurrent,
        int FuelCapacity,
        int CargoUnits,
        int CargoCapacity,
        JsonElement? Cargo,
        JsonElement? Mounts,
        JsonElement? Modules,
        JsonElement? Frame,
        JsonElement? Reactor,
        JsonElement? Engine,
        GoalSnapshotData? Goal,
        DateTimeOffset LastSyncedAt);

    private sealed record GoalSnapshotData(
        Guid Id,
        string Kind,
        int? Status,
        JsonElement? Payload);

    private sealed record ContractSnapshotData(
        string Id,
        string FactionSymbol,
        string Type,
        bool Accepted,
        bool Fulfilled,
        DateTimeOffset? Expiration,
        DateTimeOffset? DeadlineToAccept,
        DateTimeOffset? Deadline,
        JsonElement? Deliverables,
        DateTimeOffset LastSyncedAt);

    private sealed record SystemSnapshotData(
        SystemInfoSnapshotData? System,
        IReadOnlyList<WaypointSnapshotData> Waypoints);

    private sealed record SystemInfoSnapshotData(
        string Symbol,
        string SectorSymbol,
        string Type,
        int X,
        int Y);

    private sealed record WaypointSnapshotData(
        WaypointInfoSnapshotData Waypoint,
        MarketSnapshotData? Market,
        ShipyardSnapshotData? Shipyard);

    private sealed record WaypointInfoSnapshotData(
        string Symbol,
        string Type,
        int X,
        int Y,
        bool HasMarket,
        bool HasShipyard,
        JsonElement? Traits,
        DateTimeOffset LastObservedAt);

    private sealed record MarketSnapshotData(
        JsonElement? Imports,
        JsonElement? Exports,
        JsonElement? Exchange,
        JsonElement? TradeGoods,
        DateTimeOffset LastObservedAt);

    private sealed record ShipyardSnapshotData(
        JsonElement? ShipTypes,
        JsonElement? Ships,
        DateTimeOffset LastObservedAt);
}
