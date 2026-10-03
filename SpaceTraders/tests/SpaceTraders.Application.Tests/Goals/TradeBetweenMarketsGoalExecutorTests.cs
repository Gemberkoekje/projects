using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using Wolverine;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.5, rule 4: a trader reconsiders its trip with the newest prices where it lands. At the buy
/// market it buys only while the trip is still lucrative, and gives it up otherwise; at the sell market
/// it takes the cargo elsewhere, once, when selling there no longer pays and another market pays more.
/// </summary>
public sealed class TradeBetweenMarketsGoalExecutorTests
{
    private const long Credits = 129_451;

    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IMarketRefresher _refresher = Substitute.For<IMarketRefresher>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ITripBook _trips = Substitute.For<ITripBook>();
    private readonly LogRecorder _log = new();

    public TradeBetweenMarketsGoalExecutorTests()
    {
        PricesAre(Map());
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, A1, Credits, "COSMIC", 3));
    }

    private static TradeBetweenMarketsGoal Trip(bool bought = false, string sellAt = D41, bool moved = false) => new()
    {
        TradeSymbol = "EQUIPMENT",
        BuyWaypointSymbol = K85,
        SellWaypointSymbol = sellAt,
        Units = 20,
        ExpectedProfit = 4_508,
        FeedsTradeSymbol = "SHIP_PARTS",
        CargoBought = bought,
        PricePaidPerUnit = bought ? 3_254 : 0,
        SellWaypointChanged = moved,
    };

    private static ShipModel Loaded(string waypoint, int units = 20, string status = "DOCKED")
        => CommandShip(waypoint, status, cargo: [new CargoItemModel("EQUIPMENT", units)]);

    [Fact]
    public async Task ElsewhereWithoutCargo_ItFliesToTheBuyMarket()
    {
        var result = await StepAsync(CommandShip(A1), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.ShipSymbol == "SHIP-1" && c.DestinationWaypoint == K85),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeyondOneTank_ItFliesToTheFirstMarketWhereItCanRefuel()
    {
        // J57 is 496 from K85; the tank holds 400. I56 is 368 away, and 128 from K85.
        PricesAre(Map([K85Market(), D41Market(), A1Market(), .. FarMarkets()]));

        var result = await StepAsync(CommandShip(J57), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == I56),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InOrbitAtTheBuyMarket_ItDocks()
    {
        var result = await StepAsync(CommandShip(K85, "IN_ORBIT"), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _dock.Received(1).ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task AtTheBuyMarket_ItBuys_WhileTheTripIsStillLucrative()
    {
        BuyReturns(units: 20, total: 65_080);

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateCargoAsync("SHIP-1", Arg.Any<CargoModel>(), Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-1",
            Arg.Is<TradeBetweenMarketsGoal>(g => g.CargoBought && g.Units == 20 && g.PricePaidPerUnit == 3_254 && g.SellWaypointSymbol == D41),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APurchase_IsKeptWithTheTrip_AsWhatItsCargoCost()
    {
        // D46: the trip is booked when it ends, with what its cargo cost.
        BuyReturns(units: 20, total: 65_080);

        await StepAsync(CommandShip(K85), Trip());

        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", Arg.Is<TradeBetweenMarketsGoal>(g => g.Spent == 65_080 && g.Earned == 0), Arg.Any<CancellationToken>());
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task AtTheBuyMarket_ItBuysOnlyWhatTheCreditsAboveTheFuelReservePayFor()
    {
        // D24: 37,692 credits, 5,000 of them kept for fuel and 152 for the trip's own fuel, buy 10 units
        // at 3,254; without the reserve they would buy 11.
        PricesAre(Map(), credits: 37_692, fuelReserve: 5_000);
        BuyReturns(units: 10, total: 32_540);

        await StepAsync(CommandShip(K85), Trip());

        await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheBuyMarket_NothingIsBought_WithTheCreditsDownToTheFuelReserve()
    {
        // D24: what is left above the 5,000 doesn't pay for a single unit after the trip's fuel.
        PricesAre(Map(), credits: 8_254, fuelReserve: 5_000);

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeDropped" && Equals(e.Properties["Reason"], "not_possible"));
        await _trips.Received(1).BookAsync("SHIP-1", Arg.Is<TradeBetweenMarketsGoal>(booked => booked.Spent == 0 && booked.Earned == 0), "not_possible", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterBuying_ItFetchesTheBuyMarketAgain_WhileItIsStillThere()
    {
        // D25: the purchase moved the price.
        BuyReturns(units: 20, total: 65_080);

        await StepAsync(CommandShip(K85), Trip());

        await _refresher.Received(1).RefreshAfterTradeAsync(SystemSymbol, K85, "SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterSelling_ItFetchesTheSellMarketAgain_WhileItIsStillThere()
    {
        // D25: the sale moved the price.
        SellReturns(69_740);

        await StepAsync(Loaded(D41), Trip(bought: true));

        await _refresher.Received(1).RefreshAfterTradeAsync(SystemSymbol, D41, "SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheBuyMarket_ItDropsTheTrip_WhenTheNewestPriceMakesItNoLongerLucrative()
    {
        // K85 now charges 3,300: 187 a unit, minus the fuel, is under 200 a unit (D14).
        PricesAre(Map(K85Market(equipmentPrice: 3_300), D41Market(), A1Market()));

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
        var dropped = _log.Journal.Should().ContainSingle().Subject;
        dropped.EventKind.Should().Be("TradeDropped");
        dropped.Properties["Reason"].Should().Be("not_lucrative");
        dropped.Properties["BuyPrice"].Should().Be(3_300L);

        // D46: the flight to the buy market is the trip's loss.
        await _trips.Received(1).BookAsync("SHIP-1", Arg.Is<TradeBetweenMarketsGoal>(booked => booked.Spent == 0 && booked.Earned == 0), "not_lucrative", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheBuyMarket_ItDropsTheTrip_WhenItCanNoLongerBeCarriedOut()
    {
        // The sell market's prices are gone from the cache.
        PricesAre(Map(K85Market(), A1Market()));

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeDropped" && Equals(e.Properties["Reason"], "not_possible"));
    }

    [Fact]
    public async Task Loaded_ItFliesToTheSellMarket()
    {
        var result = await StepAsync(Loaded(K85), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == D41),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheSellMarket_ItSells_WhileSellingThereStillPays()
    {
        SellReturns(total: 69_740);

        var result = await StepAsync(Loaded(D41), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.Received(1).SellCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheSellMarket_ALoadLargerThanTheTradeVolume_GoesInSeveralSales()
    {
        // D41 takes 20 EQUIPMENT a time.
        SellReturns(total: 69_740);

        await StepAsync(Loaded(D41, units: 40), Trip(bought: true));

        await _port.Received(2).SellCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
        _log.Journal.Where(e => e.EventKind == "CargoSold").Should().HaveCount(2);
    }

    [Fact]
    public async Task ASoldTrip_IsBooked_WithAllItsSales_AndWhatItsCargoCost()
    {
        // D46: "the actual sell - buy - fuel", booked when the trip ends; the trip book adds the fuel.
        SellReturns(total: 69_740);

        await StepAsync(Loaded(D41, units: 40), Trip(bought: true) with { Spent = 130_160 });

        await _trips.Received(1).BookAsync(
            "SHIP-1",
            Arg.Is<TradeBetweenMarketsGoal>(booked => booked.Earned == 139_480 && booked.Spent == 130_160),
            "sold",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheSellMarket_ItTakesTheCargoElsewhere_WhenSellingThereNoLongerPays_AndAnotherMarketPaysMore()
    {
        // D41 now pays 3,300: 46 a unit over what the cargo cost. A1 pays 3,499, for 90 of fuel.
        PricesAre(Map(K85Market(), D41Market(equipmentPrice: 3_300), A1Market()));

        var result = await StepAsync(Loaded(D41), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _port.DidNotReceiveWithAnyArgs().SellCargoAsync(default!, default!, default, default);
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-1",
            Arg.Is<TradeBetweenMarketsGoal>(g => g.SellWaypointSymbol == A1 && g.SellWaypointChanged),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).InvokeAsync(Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == A1), Arg.Any<CancellationToken>());
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeRerouted" && Equals(e.Properties["SellWaypoint"], A1));
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task AtTheSellMarket_ItSellsAnyway_OnceTheSaleHasMovedBefore()
    {
        // Moved to A1 already; A1's price fell on arrival. It doesn't move again: no flying in circles.
        PricesAre(Map(K85Market(), D41Market(), A1Market() with
        {
            TradeGoods = [Good("EQUIPMENT", "IMPORT", 7_052, 3_260, 20), Good("FUEL", "EXCHANGE", 90, 76, 180)],
        }));
        SellReturns(total: 65_200);

        var result = await StepAsync(Loaded(A1), Trip(bought: true, sellAt: A1, moved: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.Received(1).SellCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheSellMarket_ItSellsAnyway_WhenNoOtherMarketPaysMore()
    {
        PricesAre(Map(K85Market(), D41Market(equipmentPrice: 3_300)));
        SellReturns(total: 66_000);

        await StepAsync(Loaded(D41), Trip(bought: true));

        await _port.Received(1).SellCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtASellMarketThatNoLongerBuysTheGood_TheTripIsDropped_OnceItHasMoved()
    {
        PricesAre(Map(K85Market(), Market(D41, Good("FUEL", "EXCHANGE", 76, 69, 180))));

        var result = await StepAsync(Loaded(D41), Trip(bought: true, moved: true) with { Spent = 65_080 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().SellCargoAsync(default!, default!, default, default);
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeDropped" && Equals(e.Properties["Reason"], "not_bought_here"));

        // D46: booked as a loss of what the cargo cost; the trading plan sells the cargo on a trip of its own.
        await _trips.Received(1).BookAsync("SHIP-1", Arg.Is<TradeBetweenMarketsGoal>(booked => booked.Spent == 65_080 && booked.Earned == 0), "not_bought_here", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNothingLeftAboard_TheTripIsDone()
    {
        var result = await StepAsync(CommandShip(D41), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().SellCargoAsync(default!, default!, default, default);
        await _trips.Received(1).BookAsync("SHIP-1", Arg.Any<TradeBetweenMarketsGoal>(), "nothing_aboard", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APurchase_IsPublished_ForTheLedgerAndTheCredits()
    {
        // B7: the ledger and the credits-spent metric read these.
        BuyReturns(units: 20, total: 65_080);

        await StepAsync(CommandShip(K85), Trip());

        await _bus.Received(1).PublishAsync(
            Arg.Is<CargoPurchasedEvent>(e => e.ShipSymbol == "SHIP-1" && e.Good.Value == "EQUIPMENT" && e.Units == 20 && e.Cost == 65_080 && e.NewAgentCredits == Credits - 65_080 && e.WaypointSymbol == K85),
            Arg.Any<DeliveryOptions>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<AgentCreditsChangedEvent>(e => e.OldCredits == Credits && e.NewCredits == Credits - 65_080),
            Arg.Any<DeliveryOptions>());
        _log.Journal.Should().ContainSingle(e => e.EventKind == "CargoBought" && Equals(e.Properties["Cost"], 65_080L) && Equals(e.Properties["WaypointSymbol"], K85));
    }

    [Fact]
    public async Task ASale_IsPublished_ForTheLedgerAndTheCredits()
    {
        SellReturns(total: 69_740);

        await StepAsync(Loaded(D41), Trip(bought: true));

        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipCargoSoldEvent>(e => e.ShipSymbol == "SHIP-1" && e.Good.Value == "EQUIPMENT" && e.Units == 20 && e.Revenue == 69_740 && e.WaypointSymbol == D41),
            Arg.Any<DeliveryOptions>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<AgentCreditsChangedEvent>(e => e.OldCredits == Credits && e.NewCredits == Credits + 69_740),
            Arg.Any<DeliveryOptions>());
        _log.Journal.Should().ContainSingle(e => e.EventKind == "CargoSold" && Equals(e.Properties["Revenue"], 69_740L));
    }

    private void PricesAre(TradeMarketMap map, long credits = Credits, long fuelReserve = 0)
        => _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(map, credits, fuelReserve: fuelReserve));

    private void BuyReturns(int units, long total)
        => _port.BuyCargoAsync("SHIP-1", "EQUIPMENT", units, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT", Credits - total, new CargoModel(units, 40, [new CargoItemModel("EQUIPMENT", units)]), total));

    private void SellReturns(long total)
        => _port.SellCargoAsync("SHIP-1", "EQUIPMENT", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT", Credits + total, new CargoModel(0, 40, []), total));

    private Task<GoalExecutionResult> StepAsync(ShipModel ship, TradeBetweenMarketsGoal trip)
        => new TradeBetweenMarketsGoalExecutor(
                _ships,
                _goals,
                _agents,
                _port,
                _tradeContexts,
                _refresher,
                _dock,
                _bus,
                _trips,
                _log.For<TradeBetweenMarketsGoalExecutor>())
            .ExecuteStepAsync(ship, trip, new ShipGoalContext(), CancellationToken.None);
}
