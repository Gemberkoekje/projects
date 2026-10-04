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
    private const long Credits = 250_000;

    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IMarketRefresher _refresher = Substitute.For<IMarketRefresher>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ITripBook _trips = Substitute.For<ITripBook>();
    private readonly FullHoldSavings _savings = new();
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
        Units = 40,
        ExpectedProfit = 9_168,
        FeedsTradeSymbol = "SHIP_PARTS",
        CargoBought = bought,
        PricePaidPerUnit = bought ? 3_254 : 0,
        SellWaypointChanged = moved,
    };

    private static ShipModel Loaded(string waypoint, int units = 40, string status = "DOCKED")
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
    public async Task ItsFlights_AskForCruise()
    {
        // Slice 6.10c: a ship left in DRIFT, a drone after its drift to a far market (D45) or a ship the navigation's fuel
        // fallback switched (B47), would trade in DRIFT, ten times slower; a trade's planned flights switch it back.
        await StepAsync(CommandShip(A1) with { FlightMode = "DRIFT" }, Trip());

        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == K85 && c.FlightMode == "CRUISE"),
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
        BuyReturns(units: 40, total: 130_160);

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 40, Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateCargoAsync("SHIP-1", Arg.Any<CargoModel>(), Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-1",
            Arg.Is<TradeBetweenMarketsGoal>(g => g.CargoBought && g.Units == 40 && g.PricePaidPerUnit == 3_254 && g.SellWaypointSymbol == D41),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuyingTheFullHoldTheCreditsWereSavedUpFor_EndsTheSaving()
    {
        // D56: ships may be bought again once that hold is bought; a saving for another route stays.
        BuyReturns(units: 40, total: 130_160);
        _savings.SaveFor("SHIP-1", TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41), 130_312);
        _savings.SaveFor("SHIP-4", TradeRoutePlanner.RouteKey("MEDICINE", D41, A1), 194_922);

        await StepAsync(CommandShip(K85), Trip());

        _savings.TryGet("SHIP-1", out _).Should().BeFalse();
        _savings.Largest().Should().Be(194_922);
    }

    [Fact]
    public async Task APurchase_IsKeptWithTheTrip_AsWhatItsCargoCost()
    {
        // D46: the trip is booked when it ends, with what its cargo cost.
        BuyReturns(units: 40, total: 130_160);

        await StepAsync(CommandShip(K85), Trip());

        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", Arg.Is<TradeBetweenMarketsGoal>(g => g.Spent == 130_160 && g.Earned == 0), Arg.Any<CancellationToken>());
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task AtTheBuyMarket_AFullHoldTheCreditsAboveTheFuelReserveDontPayFor_IsNotBought()
    {
        // D24, D56 ("full hold or nothing"): 135,311 credits, 5,000 of them kept for fuel and 152 for the trip's own fuel,
        // are one short of 40 EQUIPMENT at 3,254. Before D56 they bought 39.
        PricesAre(Map(), credits: 135_311, fuelReserve: 5_000);

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeDropped" && Equals(e.Properties["Reason"], "not_possible"));
    }

    [Fact]
    public async Task AtTheBuyMarket_TheCreditsOtherTripsHoldBack_AreNotSpent()
    {
        // D57, asked on 2026-10-03: "Let's have these credits reserved as soon as a ship starts towards it, so that this cannot
        // happen (waste of time and fuel)." SHIP-4, on its way to buy MEDICINE at D41, holds back 60,000 of the 190,000 on
        // hand: the 130,312 this trip needs, cargo and fuel, aren't there for it, and SHIP-4 still finds its own.
        PricesAre(Map(), credits: 190_000);
        TripsHoldBack(
            ("SHIP-4", new TradeBetweenMarketsGoal { TradeSymbol = "MEDICINE", BuyWaypointSymbol = D41, SellWaypointSymbol = A1, Units = 40, ReservedCredits = 60_000 }),
            ("SHIP-1", Trip() with { ReservedCredits = 130_160 }));

        var result = await StepAsync(CommandShip(K85), Trip() with { ReservedCredits = 130_160 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeDropped" && Equals(e.Properties["Reason"], "not_possible"));
    }

    [Fact]
    public async Task AtTheBuyMarket_TheCreditsAConstructionTripHoldsBack_AreNotSpent()
    {
        // Slice 6.6 (D59): the jump gate's load on its way to buy holds back 60,000 of the 190,000, as a trade trip would.
        PricesAre(Map(), credits: 190_000);
        IReadOnlyDictionary<string, SupplyConstructionGoal> construction = new Dictionary<string, SupplyConstructionGoal>
        {
            ["SHIP-6"] = new() { TradeSymbol = "FAB_MATS", ConstructionSiteWaypointSymbol = "X1-AB-I55", BuyWaypointSymbol = K85, Units = 40, ReservedCredits = 60_000 },
        };
        _goals.GetActiveConstructionGoalsAsync(Arg.Any<CancellationToken>()).Returns(construction);

        var result = await StepAsync(CommandShip(K85), Trip() with { ReservedCredits = 130_160 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task AtTheBuyMarket_ATripSpendsWhatItHeldBackItself()
    {
        // D57: of the 190,000, SHIP-4's trip holds back 50,000, which leaves 140,000; SHIP-1's own 130,160 is its to spend, and
        // SHIP-5's trip, which has bought, holds back nothing.
        PricesAre(Map(), credits: 190_000);
        BuyReturns(units: 40, total: 130_160);
        TripsHoldBack(
            ("SHIP-4", new TradeBetweenMarketsGoal { TradeSymbol = "MEDICINE", BuyWaypointSymbol = D41, SellWaypointSymbol = A1, Units = 40, ReservedCredits = 50_000 }),
            ("SHIP-1", Trip() with { ReservedCredits = 130_160 }),
            ("SHIP-5", Trip(bought: true) with { ReservedCredits = 50_000 }));

        await StepAsync(CommandShip(K85), Trip() with { ReservedCredits = 130_160 });

        await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 40, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheBuyMarket_ItDropsTheTrip_WhenTheMarketsNoLongerTradeAFullHoldAtOnce()
    {
        // D56: K85 sells EQUIPMENT 30 at a time now, fewer than the hold: buying it would take two purchases.
        PricesAre(Map(
            Market(K85, Good("EQUIPMENT", "EXPORT", 3_254, 1_456, 30), Good("FUEL", "EXCHANGE", 93, 79, 180)),
            D41Market(),
            A1Market()));

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeDropped" && Equals(e.Properties["Reason"], "not_full_hold"));
        await _trips.Received(1).BookAsync("SHIP-1", Arg.Any<TradeBetweenMarketsGoal>(), "not_full_hold", Arg.Any<CancellationToken>());
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
        BuyReturns(units: 40, total: 130_160);

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
        SellReturns(total: 139_480);

        var result = await StepAsync(Loaded(D41), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.Received(1).SellCargoAsync("SHIP-1", "EQUIPMENT", 40, Arg.Any<CancellationToken>());
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheSellMarket_ALoadLargerThanTheTradeVolume_GoesInSeveralSales()
    {
        // D41 takes 20 EQUIPMENT a time now, fewer than when the trip set off with its full hold (D56).
        PricesAre(MapWhereD41TakesTwenty());
        SellReturns(total: 69_740);

        await StepAsync(Loaded(D41, units: 40), Trip(bought: true));

        await _port.Received(2).SellCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
        _log.Journal.Where(e => e.EventKind == "CargoSold").Should().HaveCount(2);
    }

    [Fact]
    public async Task ASoldTrip_IsBooked_WithAllItsSales_AndWhatItsCargoCost()
    {
        // D46: "the actual sell - buy - fuel", booked when the trip ends; the trip book adds the fuel. Two sales: D41 takes
        // 20 at a time.
        PricesAre(MapWhereD41TakesTwenty());
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
        await _bus.Received(1).InvokeAsync(Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == A1 && c.FlightMode == "CRUISE"), Arg.Any<CancellationToken>());
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeRerouted" && Equals(e.Properties["SellWaypoint"], A1));
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task AtTheSellMarket_ItSellsAnyway_OnceTheSaleHasMovedBefore()
    {
        // Moved to A1 already; A1's price fell on arrival. It doesn't move again: no flying in circles.
        PricesAre(Map(K85Market(), D41Market(), A1Market() with
        {
            TradeGoods = [Good("EQUIPMENT", "IMPORT", 7_052, 3_260, 40), Good("FUEL", "EXCHANGE", 90, 76, 180)],
        }));
        SellReturns(total: 130_400);

        var result = await StepAsync(Loaded(A1), Trip(bought: true, sellAt: A1, moved: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.Received(1).SellCargoAsync("SHIP-1", "EQUIPMENT", 40, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheSellMarket_ItSellsAnyway_WhenNoOtherMarketPaysMore()
    {
        PricesAre(Map(K85Market(), D41Market(equipmentPrice: 3_300)));
        SellReturns(total: 132_000);

        await StepAsync(Loaded(D41), Trip(bought: true));

        await _port.Received(1).SellCargoAsync("SHIP-1", "EQUIPMENT", 40, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtASellMarketThatNoLongerBuysTheGood_TheTripIsDropped_OnceItHasMoved()
    {
        PricesAre(Map(K85Market(), Market(D41, Good("FUEL", "EXCHANGE", 76, 69, 180))));

        var result = await StepAsync(Loaded(D41), Trip(bought: true, moved: true) with { Spent = 130_160 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().SellCargoAsync(default!, default!, default, default);
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeDropped" && Equals(e.Properties["Reason"], "not_bought_here"));

        // D46: booked as a loss of what the cargo cost; the trading plan sells the cargo on a trip of its own.
        await _trips.Received(1).BookAsync("SHIP-1", Arg.Is<TradeBetweenMarketsGoal>(booked => booked.Spent == 130_160 && booked.Earned == 0), "not_bought_here", Arg.Any<CancellationToken>());
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
        BuyReturns(units: 40, total: 130_160);

        await StepAsync(CommandShip(K85), Trip());

        await _bus.Received(1).PublishAsync(
            Arg.Is<CargoPurchasedEvent>(e => e.ShipSymbol == "SHIP-1" && e.Good.Value == "EQUIPMENT" && e.Units == 40 && e.Cost == 130_160 && e.NewAgentCredits == Credits - 130_160 && e.WaypointSymbol == K85),
            Arg.Any<DeliveryOptions>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<AgentCreditsChangedEvent>(e => e.OldCredits == Credits && e.NewCredits == Credits - 130_160),
            Arg.Any<DeliveryOptions>());
        _log.Journal.Should().ContainSingle(e => e.EventKind == "CargoBought" && Equals(e.Properties["Cost"], 130_160L) && Equals(e.Properties["WaypointSymbol"], K85));
    }

    [Fact]
    public async Task ASale_IsPublished_ForTheLedgerAndTheCredits()
    {
        SellReturns(total: 139_480);

        await StepAsync(Loaded(D41), Trip(bought: true));

        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipCargoSoldEvent>(e => e.ShipSymbol == "SHIP-1" && e.Good.Value == "EQUIPMENT" && e.Units == 40 && e.Revenue == 139_480 && e.WaypointSymbol == D41),
            Arg.Any<DeliveryOptions>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<AgentCreditsChangedEvent>(e => e.OldCredits == Credits && e.NewCredits == Credits + 139_480),
            Arg.Any<DeliveryOptions>());
        _log.Journal.Should().ContainSingle(e => e.EventKind == "CargoSold" && Equals(e.Properties["Revenue"], 139_480L));
    }

    /// <summary>The fixture, with D41 taking EQUIPMENT 20 at a time: half the command ship's hold.</summary>
    private static TradeMarketMap MapWhereD41TakesTwenty()
        => Map(
            K85Market(),
            Market(D41, Good("EQUIPMENT", "IMPORT", 7_032, 3_487, 20), Good("FUEL", "EXCHANGE", 76, 69, 180)),
            A1Market());

    /// <summary>The trade trips of the fleet, by ship, as the goal store reads them (D57).</summary>
    private void TripsHoldBack(params (string Ship, TradeBetweenMarketsGoal Trip)[] trips)
    {
        IReadOnlyDictionary<string, TradeBetweenMarketsGoal> byShip = trips.ToDictionary(trip => trip.Ship, trip => trip.Trip, StringComparer.OrdinalIgnoreCase);
        _goals.GetActiveTradeGoalsAsync(Arg.Any<CancellationToken>()).Returns(byShip);
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
                _savings,
                _log.For<TradeBetweenMarketsGoalExecutor>())
            .ExecuteStepAsync(ship, trip, new ShipGoalContext(), CancellationToken.None);
}
