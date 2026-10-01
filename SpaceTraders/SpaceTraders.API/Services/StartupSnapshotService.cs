using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.API.Services;

/// <summary>
/// Saves a JSON snapshot of the agent's state at startup: the agent, its ships (with their goals),
/// its contracts, every waypoint in the systems its ships are in, and the market and shipyard where
/// each ship is.
/// </summary>
/// <remarks>
/// It runs right after startup sync and reads what sync has just cached. It calls no API itself: it
/// used to fetch all of that again, about 11 calls on every start (B35).
/// </remarks>
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
        var dbContext = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        var isInitial = !await dbContext.StartupSnapshots
            .AsNoTracking()
            .AnyAsync(s => s.AgentId == dbContext.AgentId, cancellationToken);

        var agent = await dbContext.Agents.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var ships = await dbContext.Ships.AsNoTracking().OrderBy(s => s.Symbol).ToListAsync(cancellationToken);
        var contracts = await dbContext.Contracts.AsNoTracking().OrderBy(c => c.Id).ToListAsync(cancellationToken);

        var systemSymbols = ships
            .Select(s => s.SystemSymbol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Startup sync caches the market and shipyard where each ship is, unless it is in transit.
        var shipWaypointSymbols = ships
            .Where(s => !string.Equals(s.Status, "IN_TRANSIT", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(s.WaypointSymbol))
            .Select(s => s.WaypointSymbol!)
            .ToHashSet(StringComparer.Ordinal);

        var systems = await dbContext.Systems.AsNoTracking()
            .Where(s => systemSymbols.Contains(s.Symbol))
            .ToDictionaryAsync(s => s.Symbol, StringComparer.Ordinal, cancellationToken);
        var waypoints = await dbContext.Waypoints.AsNoTracking()
            .Where(w => systemSymbols.Contains(w.SystemSymbol))
            .OrderBy(w => w.Symbol)
            .ToListAsync(cancellationToken);
        var markets = await dbContext.Markets.AsNoTracking()
            .Where(m => shipWaypointSymbols.Contains(m.WaypointSymbol))
            .ToDictionaryAsync(m => m.WaypointSymbol, StringComparer.Ordinal, cancellationToken);
        var shipyards = await dbContext.Shipyards.AsNoTracking()
            .Where(s => shipWaypointSymbols.Contains(s.WaypointSymbol))
            .ToDictionaryAsync(s => s.WaypointSymbol, StringComparer.Ordinal, cancellationToken);

        var snapshot = new GameStateSnapshotData(
            CapturedAt: TimeProvider.System.GetUtcNow(),
            Agent: agent is null ? null : ToSnapshot(agent),
            Ships: [.. ships.Select(ToSnapshot)],
            Contracts: [.. contracts.Select(ToSnapshot)],
            Systems: [.. systemSymbols.Select(symbol => new SystemSnapshotData(
                systems.TryGetValue(symbol, out var system) ? ToSnapshot(system) : null,
                [.. waypoints
                    .Where(w => w.SystemSymbol == symbol)
                    .Select(w => new WaypointSnapshotData(
                        ToSnapshot(w),
                        markets.TryGetValue(w.Symbol, out var market) ? ToSnapshot(market) : null,
                        shipyards.TryGetValue(w.Symbol, out var shipyard) ? ToSnapshot(shipyard) : null))]))]);

        dbContext.StartupSnapshots.Add(new StartupSnapshot
        {
            AgentId = dbContext.AgentId,
            SnapshotJson = JsonSerializer.Serialize(snapshot, SerializerOptions),
            CapturedAt = snapshot.CapturedAt,
            IsInitialSnapshot = isInitial,
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Startup snapshot saved (initial: {IsInitial}, ships: {ShipCount}, systems: {SystemCount}).",
            isInitial,
            ships.Count,
            snapshot.Systems.Count);
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
