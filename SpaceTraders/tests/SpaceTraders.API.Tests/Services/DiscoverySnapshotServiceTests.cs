using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceTraders.API.Services;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.API.Tests.Services;

/// <summary>
/// Slice 2.15, asked on 2026-10-04: "I'd like the snapshots to be made whenever a new discovery is made. So a shipyard with
/// a new ship type or a market with a new good type, in addition to the times they are currently made." A discovery is a
/// ship type or good new to the run: one no snapshot of it held yet (D73).
/// </summary>
public sealed class DiscoverySnapshotServiceTests : IDisposable
{
    private const string AgentId = "AGENT@2026-10-04";

    private readonly ServiceProvider _provider;
    private readonly JournalLogger _journal = new();
    private readonly GameStateSnapshots _snapshots;
    private readonly DiscoverySnapshotService _service;

    public DiscoverySnapshotServiceTests()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddSingleton<IAgentDataScope>(_ =>
        {
            var scope = new AgentDataScope();
            scope.Set(AgentId);
            return scope;
        });
        services.AddDbContext<SpaceTradersDbContext>(options => options.UseInMemoryDatabase(databaseName));
        _provider = services.BuildServiceProvider();
        _snapshots = new GameStateSnapshots(_provider.GetRequiredService<IServiceScopeFactory>(), _journal);
        _service = new DiscoverySnapshotService(_snapshots, NullLogger<DiscoverySnapshotService>.Instance);
    }

    [Fact]
    public async Task CheckAsync_AShipyardWithANewShipType_TakesASnapshotSayingWhatAndWhere()
    {
        await StoreAsync(Shipyard("X1-AB-2", "SHIP_MINING_DRONE", "SHIP_PROBE"));
        await TakeStartupSnapshotAsync();
        await StoreAsync(Shipyard("X1-AB-7", "SHIP_PROBE", "SHIP_LIGHT_HAULER"));

        var taken = await _service.CheckAsync(CancellationToken.None);

        taken.Should().NotBeNull();
        taken.Reason.Should().Be(StartupSnapshot.DiscoveryReason);
        taken.IsInitialSnapshot.Should().BeFalse();
        taken.Discovered.Should().Be("Ship types: SHIP_LIGHT_HAULER (X1-AB-7).", "the probe was known");
        var root = JsonNode.Parse(taken.SnapshotJson)!;
        root["Reason"]!.GetValue<string>().Should().Be("Discovery");
        var discovery = root["Discoveries"]!.AsArray().Should().ContainSingle().Subject!;
        discovery["Kind"]!.GetValue<string>().Should().Be("ShipType");
        discovery["Symbol"]!.GetValue<string>().Should().Be("SHIP_LIGHT_HAULER");
        discovery["Waypoints"]!.AsArray().Select(w => w!.GetValue<string>()).Should().Equal("X1-AB-7");
        _journal.Lines.Should().Equal("Discovered: Ship types: SHIP_LIGHT_HAULER (X1-AB-7). Snapshot 2 saved.");
    }

    [Fact]
    public async Task CheckAsync_AMarketWithANewGood_TakesASnapshot_HoldingThatMarket()
    {
        await StoreAsync(Market("X1-AB-2", imports: ["IRON_ORE"], exports: ["IRON"], exchange: ["FUEL"]));
        await TakeStartupSnapshotAsync();
        await StoreAsync(Market("X1-AB-9", imports: ["IRON"], exports: ["FAB_MATS"], exchange: ["FUEL"]));

        var taken = await _service.CheckAsync(CancellationToken.None);

        taken.Should().NotBeNull();
        taken.Discovered.Should().Be("Goods: FAB_MATS (X1-AB-9).");
        var root = JsonNode.Parse(taken.SnapshotJson)!;
        var markets = root["Systems"]!.AsArray()
            .SelectMany(system => system!["Waypoints"]!.AsArray())
            .Where(waypoint => waypoint!["Market"] is not null)
            .Select(waypoint => waypoint!["Waypoint"]!["Symbol"]!.GetValue<string>());
        markets.Should().Equal("X1-AB-2", "X1-AB-9");
    }

    [Fact]
    public async Task CheckAsync_ANewPlaceWithKnownTypes_IsNoDiscovery()
    {
        // New to the run, not new at that place (D73): a second shipyard selling a known type, or a market trading known
        // goods, discovers nothing.
        await StoreAsync(Shipyard("X1-AB-2", "SHIP_PROBE"), Market("X1-AB-2", imports: ["IRON_ORE"], exports: [], exchange: ["FUEL"]));
        await TakeStartupSnapshotAsync();
        await StoreAsync(Shipyard("X1-AB-7", "SHIP_PROBE"), Market("X1-AB-7", imports: [], exports: ["IRON_ORE"], exchange: ["FUEL"]));

        var taken = await _service.CheckAsync(CancellationToken.None);

        taken.Should().BeNull();
        (await SnapshotsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task CheckAsync_TakesOneSnapshotPerDiscovery()
    {
        await TakeStartupSnapshotAsync();
        await StoreAsync(Market("X1-AB-9", imports: [], exports: ["FAB_MATS"], exchange: []));

        var first = await _service.CheckAsync(CancellationToken.None);
        var second = await _service.CheckAsync(CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().BeNull("FAB_MATS is in the snapshot just taken");
        (await SnapshotsAsync()).Select(s => s.Reason).Should().Equal(StartupSnapshot.StartupReason, StartupSnapshot.DiscoveryReason);
    }

    [Fact]
    public async Task CheckAsync_DiscoveriesBetweenTwoLooks_ShareOneSnapshot()
    {
        // The priced goods count too: an arrival stores them with the market's lists.
        await TakeStartupSnapshotAsync();
        await StoreAsync(
            Shipyard("X1-AB-7", "SHIP_SIPHON_DRONE"),
            Market("X1-AB-9", imports: [], exports: ["FAB_MATS"], exchange: [], priced: ["FAB_MATS", "ADVANCED_CIRCUITRY"]),
            Market("X1-CD-1", imports: ["FAB_MATS"], exports: [], exchange: []));

        var taken = await _service.CheckAsync(CancellationToken.None);

        taken!.Discovered.Should().Be(
            "Ship types: SHIP_SIPHON_DRONE (X1-AB-7). Goods: ADVANCED_CIRCUITRY (X1-AB-9), FAB_MATS (X1-AB-9, X1-CD-1).");
        (await SnapshotsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task CheckAsync_WithoutAStartupSnapshot_StartsFromWhatTheCacheListsAtItsFirstLook()
    {
        // The startup snapshot failed: what the cache listed then isn't a discovery, but what it lists after is.
        await StoreAsync(Shipyard("X1-AB-2", "SHIP_PROBE"));

        var first = await _service.CheckAsync(CancellationToken.None);
        await StoreAsync(Shipyard("X1-AB-7", "SHIP_MINING_DRONE"));
        var second = await _service.CheckAsync(CancellationToken.None);

        first.Should().BeNull();
        second!.Discovered.Should().Be("Ship types: SHIP_MINING_DRONE (X1-AB-7).");
        second.IsInitialSnapshot.Should().BeTrue("it is the agent's first");
    }

    [Fact]
    public async Task CheckAsync_ListsInTheCacheThatDoNotParse_DiscoverNothing()
    {
        await TakeStartupSnapshotAsync();
        await StoreAsync(new CachedShipyard { AgentId = AgentId, WaypointSymbol = "X1-AB-7", SystemSymbol = "X1-AB", ShipTypesJson = "not json" });

        (await _service.CheckAsync(CancellationToken.None)).Should().BeNull();
    }

    public void Dispose()
    {
        _service.Dispose();
        _provider.Dispose();
    }

    private static CachedShipyard Shipyard(string waypoint, params string[] shipTypes) => new()
    {
        AgentId = AgentId,
        WaypointSymbol = waypoint,
        SystemSymbol = SystemOf(waypoint),
        ShipTypesJson = $"[{string.Join(",", shipTypes.Select(type => $$"""{"type":"{{type}}"}"""))}]",
    };

    private static CachedMarket Market(string waypoint, string[] imports, string[] exports, string[] exchange, string[]? priced = null) => new()
    {
        AgentId = AgentId,
        WaypointSymbol = waypoint,
        SystemSymbol = SystemOf(waypoint),
        ImportsJson = Goods(imports),
        ExportsJson = Goods(exports),
        ExchangeJson = Goods(exchange),
        TradeGoodsJson = priced is null ? null : $"[{string.Join(",", priced.Select(good => $$"""{"symbol":"{{good}}","type":"EXPORT","purchasePrice":100,"sellPrice":90}"""))}]",
    };

    private static string Goods(string[] symbols) => $"[{string.Join(",", symbols.Select(symbol => $$"""{"symbol":"{{symbol}}"}"""))}]";

    private static string SystemOf(string waypoint) => waypoint[..waypoint.LastIndexOf('-')];

    private async Task TakeStartupSnapshotAsync()
        => await new StartupSnapshotService(_snapshots, NullLogger<StartupSnapshotService>.Instance).StartAsync(CancellationToken.None);

    /// <summary>Stores the shipyards and markets with their waypoints, as the bot finds them through the cached waypoints.</summary>
    private async Task StoreAsync(params object[] rows)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        var cached = (await db.Waypoints.Select(w => w.Symbol).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var (waypoint, system) = row switch
            {
                CachedMarket market => (market.WaypointSymbol, market.SystemSymbol),
                CachedShipyard shipyard => (shipyard.WaypointSymbol, shipyard.SystemSymbol),
                _ => (null, null),
            };
            if (waypoint is not null && cached.Add(waypoint))
            {
                db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = waypoint, SystemSymbol = system!, Type = "ORBITAL_STATION" });
            }

            db.Add(row);
        }

        await db.SaveChangesAsync();
    }

    private async Task<List<StartupSnapshot>> SnapshotsAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>().StartupSnapshots.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
    }

    private sealed class JournalLogger : ILogger<GameStateSnapshots>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> properties && properties.Any(p => p.Key == "EventKind"))
            {
                Lines.Add(formatter(state, exception));
            }
        }
    }
}
