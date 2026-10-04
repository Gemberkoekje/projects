using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using SpaceTraders.Infrastructure.Persistence.Scoping;

namespace SpaceTraders.API.Tests.Services;

/// <summary>
/// Slice 2.8: the markets dashboard reads the markets and shipyards the bot has cached, and the
/// game's production chains, which are fetched once.
/// </summary>
public sealed class PrometheusMarketMetricsServiceTests
{
    private const string AgentId = "AGENT@2026-09-27";
    private static readonly DateTimeOffset Start = new(2026, 10, 02, 10, 00, 00, TimeSpan.Zero);

    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();

    [Fact]
    public async Task SampleAsync_ExportsTheCachedMarketsAndShipyards()
    {
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Waypoints.Add(Waypoint("X1-AB-H51", "PLANET"));
            db.Waypoints.Add(Waypoint("X1-AB-H52", "MOON"));

            // Visited: the market as the API returned it, in camelCase.
            db.Markets.Add(new CachedMarket
            {
                AgentId = AgentId,
                WaypointSymbol = "X1-AB-H51",
                SystemSymbol = "X1-AB",
                LastObservedAt = Start,
                TradeGoodsJson = """[{"symbol":"COPPER_ORE","type":"IMPORT","tradeVolume":60,"supply":"SCARCE","activity":"WEAK","purchasePrice":60,"sellPrice":55}]""",
                ImportsJson = """[{"symbol":"COPPER_ORE"}]""",
            });

            // Not visited yet: no prices.
            db.Markets.Add(new CachedMarket { AgentId = AgentId, WaypointSymbol = "X1-AB-H52", SystemSymbol = "X1-AB", LastObservedAt = Start.AddMinutes(-5) });
            db.Shipyards.Add(new CachedShipyard
            {
                AgentId = AgentId,
                WaypointSymbol = "X1-AB-H52",
                SystemSymbol = "X1-AB",
                LastObservedAt = Start,
                ShipTypesJson = """[{"type":"SHIP_MINING_DRONE"},{"type":"SHIP_PROBE"}]""",
                ShipsDetailJson = """[{"type":"SHIP_MINING_DRONE","supply":"MODERATE","purchasePrice":46885}]""",
            });
            await db.SaveChangesAsync();
        }

        IReadOnlyCollection<MarketMetricsSample> markets = [];
        IReadOnlyCollection<ShipyardMetricsSample> shipyards = [];
        _metrics.When(m => m.Markets(Arg.Any<IReadOnlyCollection<MarketMetricsSample>>()))
            .Do(call => markets = call.Arg<IReadOnlyCollection<MarketMetricsSample>>());
        _metrics.When(m => m.Shipyards(Arg.Any<IReadOnlyCollection<ShipyardMetricsSample>>()))
            .Do(call => shipyards = call.Arg<IReadOnlyCollection<ShipyardMetricsSample>>());

        await Service(provider).SampleAsync(Start, CancellationToken.None);

        markets.Should().HaveCount(2);
        var visited = markets.Single(m => m.Waypoint == "X1-AB-H51");
        visited.System.Should().Be("X1-AB");
        visited.WaypointType.Should().Be("PLANET");
        visited.ObservedAt.Should().Be(Start);
        visited.Goods.Should().Equal(new TradeGoodSnapshot("COPPER_ORE", "IMPORT", 60, 55, 60, "SCARCE", "WEAK"));
        markets.Single(m => m.Waypoint == "X1-AB-H52").Goods.Should().BeEmpty();

        var shipyard = shipyards.Should().ContainSingle().Subject;
        shipyard.WaypointType.Should().Be("MOON");
        shipyard.ObservedAt.Should().Be(Start);
        shipyard.ShipTypes.Should().Equal("SHIP_MINING_DRONE", "SHIP_PROBE");
        shipyard.Ships.Should().ContainSingle().Which.PurchasePrice.Should().Be(46_885);
    }

    [Fact]
    public async Task ASystemOnlyTheCommandShipExplored_KeepsItsSummary_ButNotEachGoodsSeries()
    {
        // Asked on 2026-10-04: exploring has no limit, and each explored system's goods would add about a thousand series.
        using var provider = BuildProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-AB-H51", SystemSymbol = "X1-AB", Type = "PLANET", HasMarket = true });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-KR90-A1", SystemSymbol = "X1-KR90", Type = "PLANET", HasMarket = true });
            db.Waypoints.Add(new CachedWaypoint { AgentId = AgentId, Symbol = "X1-KR90-R1", SystemSymbol = "X1-KR90", Type = "ASTEROID", TraitsJson = """[{"symbol":"COMMON_METAL_DEPOSITS"}]""" });
            foreach (var (waypoint, system) in new[] { ("X1-AB-H51", "X1-AB"), ("X1-KR90-A1", "X1-KR90") })
            {
                db.Markets.Add(new CachedMarket
                {
                    AgentId = AgentId,
                    WaypointSymbol = waypoint,
                    SystemSymbol = system,
                    LastObservedAt = Start,
                    TradeGoodsJson = """[{"symbol":"IRON_ORE","type":"IMPORT","tradeVolume":60,"supply":"SCARCE","activity":"WEAK","purchasePrice":60,"sellPrice":55}]""",
                });
            }

            await db.SaveChangesAsync();
            await seedScope.ServiceProvider.GetRequiredService<IAgentRepository>().UpsertAsync(new AgentModel("AGENT", null, "X1-AB-A1", 100_000, "COSMIC", 2));
            await seedScope.ServiceProvider.GetRequiredService<IShipRepository>().UpsertAsync(new ShipModel("AGENT-2", "X1-AB", "X1-AB-H51", "DOCKED", "CRUISE", 80, 80));
            await seedScope.ServiceProvider.GetRequiredService<IPlanRepository>().UpsertAsync("Explore", new ExplorePlanState
            {
                ShipSymbol = "AGENT-1",
                HomeSystemSymbol = "X1-AB",
                Status = ExploreStatus.Exploring,
                UpdatedAt = Start,
                Systems =
                [
                    new KnownSystem { SystemSymbol = "X1-AB", GateWaypointSymbol = "X1-AB-I1", Gate = GateState.Active, Connections = ["X1-KR90-AF5F"], ExploredAt = Start },
                    new KnownSystem { SystemSymbol = "X1-KR90", GateWaypointSymbol = "X1-KR90-AF5F", Gate = GateState.Active, ExploredAt = Start },
                ],
            });
        }

        IReadOnlyCollection<MarketMetricsSample> markets = [];
        IReadOnlyCollection<SystemSample> systems = [];
        _metrics.When(m => m.Markets(Arg.Any<IReadOnlyCollection<MarketMetricsSample>>()))
            .Do(call => markets = call.Arg<IReadOnlyCollection<MarketMetricsSample>>());
        _metrics.When(m => m.Systems(Arg.Any<IReadOnlyCollection<SystemSample>>()))
            .Do(call => systems = call.Arg<IReadOnlyCollection<SystemSample>>());

        await Service(provider).SampleAsync(Start, CancellationToken.None);

        markets.Single(m => m.Waypoint == "X1-AB-H51").Goods.Should().ContainSingle("home, where our ships work, keeps each good");
        markets.Single(m => m.Waypoint == "X1-KR90-A1").Goods.Should().BeEmpty();
        var kr90 = systems.Single(system => system.System == "X1-KR90");
        kr90.State.Should().Be("explored");
        kr90.Jumps.Should().Be(1);
        kr90.Markets.Should().Be(1);
        kr90.GatheringSites.Should().ContainKey("IRON_ORE");
        kr90.RawGoods.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Good = "IRON_ORE", Price = 55, Market = "X1-KR90-A1", Supply = "SCARCE" });
        systems.Single(system => system.System == "X1-AB").State.Should().Be("home");
    }

    [Fact]
    public async Task SampleAsync_ExportsTheProductionChainsOnce()
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> chains = new Dictionary<string, IReadOnlyList<string>> { ["IRON"] = ["IRON_ORE"] };
        _port.GetSupplyChainAsync(Arg.Any<CancellationToken>()).Returns(chains);
        using var provider = BuildProvider();
        var service = Service(provider);

        await service.SampleAsync(Start, CancellationToken.None);
        await service.SampleAsync(Start.AddMinutes(1), CancellationToken.None);

        await _port.Received(1).GetSupplyChainAsync(Arg.Any<CancellationToken>());
        _metrics.Received(1).SupplyChain(Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
            exported => exported.Count == 1 && exported["IRON"].SequenceEqual(new[] { "IRON_ORE" })));
    }

    [Fact]
    public async Task SampleAsync_AsksForTheProductionChainsAgainAnHourAfterTheApiFailed()
    {
        _port.GetSupplyChainAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("502 Bad Gateway"));
        using var provider = BuildProvider();
        var service = Service(provider);

        await service.SampleAsync(Start, CancellationToken.None);
        await service.SampleAsync(Start.AddMinutes(30), CancellationToken.None);
        await _port.Received(1).GetSupplyChainAsync(Arg.Any<CancellationToken>());

        await service.SampleAsync(Start.AddHours(1), CancellationToken.None);
        await _port.Received(2).GetSupplyChainAsync(Arg.Any<CancellationToken>());
        _metrics.DidNotReceiveWithAnyArgs().SupplyChain(default!);
    }

    private PrometheusMarketMetricsService Service(ServiceProvider provider)
        => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _metrics,
            provider.GetRequiredService<ISupplyChainCache>(),
            NullLogger<PrometheusMarketMetricsService>.Instance);

    private ServiceProvider BuildProvider()
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
        services.AddScoped<IMarketRepository, MarketRepository>();
        services.AddScoped<IShipyardRepository, ShipyardRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IShipRepository, ShipRepository>();
        services.AddScoped<IShipAssignmentRepository, ShipAssignmentRepository>();
        services.AddScoped(_ => _port);
        services.AddSingleton<ISupplyChainCache>(_ => new SupplyChainCache(NullLogger<SupplyChainCache>.Instance));
        return services.BuildServiceProvider();
    }

    private static CachedWaypoint Waypoint(string symbol, string type)
        => new() { AgentId = AgentId, Symbol = symbol, SystemSymbol = "X1-AB", Type = type };
}
