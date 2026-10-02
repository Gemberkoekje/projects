using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>D25: after a purchase or a sale the market is fetched again while the ship is still there.</summary>
public sealed class MarketRefresherTests
{
    private const string Prices = """[{"symbol":"COPPER_ORE","type":"IMPORT","tradeVolume":123,"supply":"LIMITED","purchasePrice":138,"sellPrice":61}]""";

    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly LogRecorder _log = new();

    [Fact]
    public async Task AfterATrade_TheMarketIsStored_AndItsPricesRecorded()
    {
        var market = new MarketDataModel("X1-AB-H51", "X1-AB", Prices, null, null, null);
        _port.GetMarketAsync("X1-AB", "X1-AB-H51", Arg.Any<CancellationToken>()).Returns(market);

        await Refresher().RefreshAfterTradeAsync("X1-AB", "X1-AB-H51", "SHIP-3", CancellationToken.None);

        await _markets.Received(1).UpsertAsync(market, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(Arg.Is<MarketDataRefreshedEvent>(e => e.Waypoint.Value == "X1-AB-H51"), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task AFailedFetch_IsLogged_AndTheTradeStands()
    {
        _port.GetMarketAsync("X1-AB", "X1-AB-H51", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("502"));

        var refresh = () => Refresher().RefreshAfterTradeAsync("X1-AB", "X1-AB-H51", "SHIP-3", CancellationToken.None);

        await refresh.Should().NotThrowAsync();
        _log.Entries.Should().ContainSingle(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task AnAnswerWithoutPrices_LeavesTheCachedPricesAlone()
    {
        _port.GetMarketAsync("X1-AB", "X1-AB-H51", Arg.Any<CancellationToken>())
            .Returns(new MarketDataModel("X1-AB-H51", "X1-AB", null, null, null, null));

        (await Refresher().RefreshAsync("X1-AB", "X1-AB-H51", CancellationToken.None)).Should().BeFalse();

        await _markets.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    private MarketRefresher Refresher() => new(_port, _markets, _bus, _log.For<MarketRefresher>());
}
