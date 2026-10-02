using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.API.Services;
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
        services.AddScoped(_ => _port);
        services.AddSingleton<ISupplyChainCache>(_ => new SupplyChainCache(NullLogger<SupplyChainCache>.Instance));
        return services.BuildServiceProvider();
    }

    private static CachedWaypoint Waypoint(string symbol, string type)
        => new() { AgentId = AgentId, Symbol = symbol, SystemSymbol = "X1-AB", Type = type };
}
