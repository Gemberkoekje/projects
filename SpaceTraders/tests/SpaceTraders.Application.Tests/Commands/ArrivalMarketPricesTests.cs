using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>B62: an arrival stores a market's prices, never an answer without them.</summary>
public sealed class ArrivalMarketPricesTests
{
    private const string Ship = "SPECTER-5";
    private const string System = "X1-FJ91";
    private const string Station = "X1-FJ91-C46";

    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    public ArrivalMarketPricesTests()
    {
        _ships.FindAsync(Ship, Arg.Any<CancellationToken>()).Returns(new ShipModel(Ship, System, Station, "IN_ORBIT", "CRUISE", 0, 80));
        _waypoints.FindAsync(Station, Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel(Station, System, "ORBITAL_STATION", 63, 143, HasMarket: true, HasShipyard: false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task AnArrivalAnsweredWithoutPrices_KeepsThePricesTheCacheHas()
    {
        // B62, seen on the cluster on 2026-10-04: when the pod started at 19:28Z, the scheduler fired SPECTER-5's arrival at
        // X1-FJ91-C46, the station at the gas giant C45, due since 19:27:37Z. Its market came back without prices, and the
        // arrival stored that: the cache lost C46's fuel price. At 19:29:59Z SPECTER-6, at C45 with 37 of 80 fuel, found no
        // fuel stop it could reach (C47 is 43 away; before, it refuelled at C46 first), flew straight at G59, and drifted
        // for 43 minutes once the API refused the flight with a 400.
        _port.GetMarketAsync(System, Station, Arg.Any<CancellationToken>())
            .Returns(new MarketDataModel(Station, System, null, "[{\"symbol\":\"ELECTRONICS\"}]", "[]", "[{\"symbol\":\"FUEL\"}]"));

        await HandleAsync();

        await _markets.DidNotReceive().UpsertAsync(Arg.Any<MarketDataModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnArrivalAnsweredWithPrices_StoresThem_AndRecordsThem()
    {
        var market = new MarketDataModel(Station, System, "[{\"symbol\":\"FUEL\",\"purchasePrice\":72}]", "[]", "[]", "[{\"symbol\":\"FUEL\"}]");
        _port.GetMarketAsync(System, Station, Arg.Any<CancellationToken>()).Returns(market);

        await HandleAsync();

        await _markets.Received(1).UpsertAsync(market, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<MarketDataRefreshedEvent>(refreshed => refreshed.TradeGoodsJson == market.TradeGoodsJson),
            Arg.Any<DeliveryOptions?>());
    }

    private Task HandleAsync()
        => new NavigateToWaypointArrivedHandler(
                _ships,
                _waypoints,
                new MarketRefresher(_port, _markets, _bus, NullLogger<MarketRefresher>.Instance),
                Substitute.For<IShipyardRepository>(),
                Substitute.For<IDockSubCommand>(),
                _port,
                _bus,
                NullLogger<NavigateToWaypointArrivedHandler>.Instance)
            .Handle(new NavigateToWaypointArrivedCommand(Ship, Station, Guid.NewGuid()), CancellationToken.None);
}
