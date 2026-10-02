using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.5, as asked on 2026-10-02: any market that has one of our ships at its waypoint refreshes
/// once every <c>Market.RefreshMinutes</c>. The API shows a market's prices only while a ship is there.
/// A refresh is a read, which can go later without loss (D19): one market a tick, the most overdue.
/// </summary>
public sealed class MarketWatchServiceTests
{
    private const string System = "X1-AB";
    private const string Market = "X1-AB-H52";
    private const string Other = "X1-AB-A1";
    private const string Asteroid = "X1-AB-B7";
    private const string Prices = """[{"symbol":"FUEL","type":"EXCHANGE","tradeVolume":180,"supply":"SCARCE","purchasePrice":76,"sellPrice":69}]""";

    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly MarketWatchAttempts _attempts = new();
    private readonly LogRecorder _log = new();

    public MarketWatchServiceTests()
    {
        _settings.GetAsync<int>("Market.RefreshMinutes", Arg.Any<CancellationToken>()).Returns(5);
        foreach (var market in new[] { Market, Other })
        {
            _waypoints.FindAsync(market, Arg.Any<CancellationToken>()).Returns(Waypoint(market, hasMarket: true));
            _port.GetMarketAsync(System, market, Arg.Any<CancellationToken>())
                .Returns(new MarketDataModel(market, System, Prices, null, null, """[{"symbol":"FUEL"}]"""));
        }

        _waypoints.FindAsync(Asteroid, Arg.Any<CancellationToken>()).Returns(Waypoint(Asteroid, hasMarket: false));
    }

    [Fact]
    public async Task AMarketWithAShipAtIt_IsRefreshed_OnceItsPricesAreDue()
    {
        Fleet(Probe("SHIP-2", Market));
        LastSeen(Market, minutesAgo: 6);

        await RefreshAsync();

        await _markets.Received(1).UpsertAsync(Arg.Is<MarketDataModel>(m => m.WaypointSymbol == Market && m.TradeGoodsJson == Prices), Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<MarketDataRefreshedEvent>(e => e.Waypoint.Value == Market && e.TradeGoodsJson == Prices),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task AMarketSeenWithinTheInterval_IsLeftAlone()
    {
        // An arrival refreshed it a minute ago.
        Fleet(Probe("SHIP-2", Market));
        LastSeen(Market, minutesAgo: 1);

        await RefreshAsync();

        await _port.DidNotReceiveWithAnyArgs().GetMarketAsync(default!, default!, default);
    }

    [Fact]
    public async Task OneMarketATick_TheOneThatHasWaitedLongest()
    {
        // D19: a refresh is a read; the watch never holds the fleet up with a run of them.
        Fleet(Probe("SHIP-1", Other), Probe("SHIP-2", Market));
        LastSeen(Other, minutesAgo: 6);
        LastSeen(Market, minutesAgo: 30);

        await RefreshAsync();

        await _port.Received(1).GetMarketAsync(System, Market, Arg.Any<CancellationToken>());
        await _port.DidNotReceive().GetMarketAsync(System, Other, Arg.Any<CancellationToken>());

        await RefreshAsync();

        await _port.Received(1).GetMarketAsync(System, Other, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeveralShipsAtOneMarket_RefreshItOnce()
    {
        Fleet(Probe("SHIP-2", Market), Probe("SHIP-5", Market));
        LastSeen(Market, minutesAgo: 30);

        await RefreshAsync();
        await RefreshAsync();

        await _port.Received(1).GetMarketAsync(System, Market, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AShipInTransit_OrAtAWaypointWithoutAMarket_RefreshesNothing()
    {
        Fleet(
            Probe("SHIP-2", Market) with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(3) },
            Probe("SHIP-3", Asteroid));

        await RefreshAsync();

        await _port.DidNotReceiveWithAnyArgs().GetMarketAsync(default!, default!, default);
    }

    [Fact]
    public async Task ZeroMinutes_SwitchesTheWatchOff()
    {
        _settings.GetAsync<int>("Market.RefreshMinutes", Arg.Any<CancellationToken>()).Returns(0);
        Fleet(Probe("SHIP-2", Market));

        await RefreshAsync();

        await _port.DidNotReceiveWithAnyArgs().GetMarketAsync(default!, default!, default);
    }

    [Fact]
    public async Task AnAnswerWithoutPrices_LeavesTheCachedPricesAlone_AndWaitsAnInterval()
    {
        // Without a ship of ours there the API leaves the prices out; storing it would wipe them.
        _port.GetMarketAsync(System, Market, Arg.Any<CancellationToken>())
            .Returns(new MarketDataModel(Market, System, null, null, null, """[{"symbol":"FUEL"}]"""));
        Fleet(Probe("SHIP-2", Market));

        await RefreshAsync();
        await RefreshAsync();

        await _port.Received(1).GetMarketAsync(System, Market, Arg.Any<CancellationToken>());
        await _markets.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
        await _bus.DidNotReceive().PublishAsync(Arg.Any<MarketDataRefreshedEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task AMarketThatFails_WaitsAnInterval_WhileTheOthersGoAhead()
    {
        // Never seen, both; A1 comes first and fails. It mustn't stay the most overdue on every tick.
        _port.GetMarketAsync(System, Other, Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("500"));
        Fleet(Probe("SHIP-1", Other), Probe("SHIP-2", Market));

        await RefreshAsync();
        await RefreshAsync();
        await RefreshAsync();

        await _port.Received(1).GetMarketAsync(System, Other, Arg.Any<CancellationToken>());
        await _markets.Received(1).UpsertAsync(Arg.Is<MarketDataModel>(m => m.WaypointSymbol == Market), Arg.Any<CancellationToken>());
        _log.Entries.Should().ContainSingle(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    private static ShipModel Probe(string symbol, string waypoint)
        => new(symbol, System, waypoint, "DOCKED", "DRIFT", 0, 0, ShipType: "SATELLITE");

    private static WaypointCacheModel Waypoint(string symbol, bool hasMarket)
        => new(symbol, System, "MOON", 0, 0, hasMarket, false, DateTimeOffset.UnixEpoch);

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private void LastSeen(string waypoint, int minutesAgo)
        => _markets.GetLastObservedAtAsync(waypoint, Arg.Any<CancellationToken>()).Returns(DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));

    private Task RefreshAsync()
        => new MarketWatchService(_ships, _waypoints, _markets, new MarketRefresher(_port, _markets, _bus, _log.For<MarketRefresher>()), _settings, _attempts, _log.For<MarketWatchService>())
            .RefreshDueMarketAsync(CancellationToken.None);
}
