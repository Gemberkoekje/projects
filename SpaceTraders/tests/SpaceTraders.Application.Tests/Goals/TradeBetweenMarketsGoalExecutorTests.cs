using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Tests.Trading;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using Wolverine;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.5, rule 4: a trader reconsiders its trip with the newest prices where it lands. At the buy
/// market it buys only while the trip is still lucrative, and gives it up otherwise; at the sell market
/// it takes the cargo elsewhere, once, when selling there no longer pays and another market pays more. D79: it buys and
/// sells a batch of the trade volume at a time, each at the price quoted then, while the next still earns the minimum.
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
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IGateNetwork _gates = Substitute.For<IGateNetwork>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly IRefuelSubCommand _refuel = Substitute.For<IRefuelSubCommand>();
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
    public async Task ItsFlights_NoLongerDrift()
    {
        // Slice 6.10c: a ship left in DRIFT, a drone after its drift to a far market (D45) or a ship the navigation's fuel
        // fallback switched (B47), would trade in DRIFT, ten times slower; a trade's planned flights switch it out of DRIFT,
        // here into BURN: K85 sells fuel, and a full tank pays for the 104 twice over (D84).
        await StepAsync(CommandShip(A1) with { FlightMode = "DRIFT" }, Trip());

        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == K85 && c.FlightMode == "BURN"),
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

        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", Arg.Is<TradeBetweenMarketsGoal>(g => g.CargoBought && g.Spent == 130_160 && g.Earned == 0), Arg.Any<CancellationToken>());
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task AtTheBuyMarket_TheCreditsAboveTheFuelReserve_BuyWhatTheyPayFor()
    {
        // D24, D79: 135,311 credits, 5,000 of them kept for fuel and 152 for the trip's own fuel, are one short of 40 EQUIPMENT
        // at 3,254: the trip buys 39.
        PricesAre(Map(), credits: 135_311, fuelReserve: 5_000);
        BuyReturns(units: 39, total: 39 * 3_254);

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 39, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheBuyMarket_TheCreditsOtherTripsHoldBack_AreNotSpent()
    {
        // D57, asked on 2026-10-03: "Let's have these credits reserved as soon as a ship starts towards it, so that this cannot
        // happen (waste of time and fuel)." SHIP-4, on its way to buy MEDICINE at D41, holds back 60,000 of the 190,000 on
        // hand: of the 130,312 this trip would need, cargo and fuel, 130,000 are there for it, which buy 39 (D79), and SHIP-4
        // still finds its own.
        PricesAre(Map(), credits: 190_000);
        BuyReturns(units: 39, total: 39 * 3_254);
        TripsHoldBack(
            ("SHIP-4", new TradeBetweenMarketsGoal { TradeSymbol = "MEDICINE", BuyWaypointSymbol = D41, SellWaypointSymbol = A1, Units = 40, ReservedCredits = 60_000 }),
            ("SHIP-1", Trip() with { ReservedCredits = 130_160 }));

        await StepAsync(CommandShip(K85), Trip() with { ReservedCredits = 130_160 });

        await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 39, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheBuyMarket_TheCreditsAConstructionTripHoldsBack_AreNotSpent()
    {
        // Slice 6.6 (D64): the jump gate's load on its way to buy holds back 60,000 of the 190,000, as a trade trip would: the
        // trip buys the 39 the rest pays for.
        PricesAre(Map(), credits: 190_000);
        BuyReturns(units: 39, total: 39 * 3_254);
        IReadOnlyDictionary<string, SupplyConstructionGoal> construction = new Dictionary<string, SupplyConstructionGoal>
        {
            ["SHIP-6"] = new() { TradeSymbol = "FAB_MATS", ConstructionSiteWaypointSymbol = "X1-AB-I55", BuyWaypointSymbol = K85, Units = 40, ReservedCredits = 60_000 },
        };
        _goals.GetActiveConstructionGoalsAsync(Arg.Any<CancellationToken>()).Returns(construction);

        await StepAsync(CommandShip(K85), Trip() with { ReservedCredits = 130_160 });

        await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 39, Arg.Any<CancellationToken>());
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
    public async Task AtTheBuyMarket_ItBuysInBatchesOfTheTradeVolume_WhileEachStillEarnsTheMinimum()
    {
        // D79: K85 sells EQUIPMENT 20 at a time. The first 20 cost 3,254, 233 under D41's 3,487; after that purchase K85
        // quotes 3,280, which still earns 207, so the next 20 go too.
        BuyQuotes(3_254, 3_280);

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(2).BuyCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
        await _refresher.Received(2).RefreshAfterTradeAsync(SystemSymbol, K85, "SHIP-1", Arg.Any<CancellationToken>());
        const long spent = (20 * 3_254) + (20 * 3_280);
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-1",
            Arg.Is<TradeBetweenMarketsGoal>(g => g.CargoBought && g.Units == 40 && g.Spent == spent && g.PricePaidPerUnit == spent / 40),
            Arg.Any<CancellationToken>());
        _log.Journal.Where(e => e.EventKind == "CargoBought").Should().HaveCount(2);
    }

    [Fact]
    public async Task AtTheBuyMarket_ItStopsBuying_OnceTheNextBatchWouldEarnTooLittle()
    {
        // D79: after the first 20, K85 quotes 3,300, 187 under D41's price: under the 200 a unit, so the trip goes on with 20.
        BuyQuotes(3_254, 3_300);

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", Arg.Is<TradeBetweenMarketsGoal>(g => g.CargoBought && g.Units == 20), Arg.Any<CancellationToken>());
        _log.Journal.Should().NotContain(e => e.EventKind == "TradeDropped");
    }

    [Fact]
    public async Task BetweenBatches_TheTripIsStored_WithWhatItSpent_AndHoldsBackOnlyWhatIsLeftToBuy()
    {
        // D57, D79: a restart between batches goes on from what is aboard, with what the earlier batches cost.
        BuyQuotes(3_254, 3_300);

        await StepAsync(CommandShip(K85), Trip() with { ReservedCredits = 130_160 });

        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-1",
            Arg.Is<TradeBetweenMarketsGoal>(g => !g.CargoBought && g.Spent == 65_080 && g.ReservedCredits == 130_160 - 65_080),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterARestartBetweenBatches_ItBuysWhatIsLeft_WithoutWeighingTheTripAgain()
    {
        // The first 20 were bought and stored; K85 now quotes 3,280, which still earns the minimum for the next 20.
        BuyQuotes(3_280);

        await StepAsync(CommandShip(K85, cargo: [new CargoItemModel("EQUIPMENT", 20)]), Trip() with { Spent = 65_080 });

        await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-1",
            Arg.Is<TradeBetweenMarketsGoal>(g => g.CargoBought && g.Units == 40 && g.Spent == 65_080 + (20 * 3_280)),
            Arg.Any<CancellationToken>());
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AtTheBuyMarket_ATripWhoseGoodsSellForWhatTheyCost_BuysOnlyWhenItFeedsTheJumpGate(bool gateNeedsShipParts)
    {
        // D90: K85 now charges 3,487, what D41 pays. D41 makes SHIP_PARTS from EQUIPMENT: while the gate needs them, the trip
        // buys all the same, its fuel lost; otherwise it earns nothing a unit, under 200 (D14), and is dropped.
        PricesAre(GateNeeds(gateNeedsShipParts, K85Market(equipmentPrice: 3_487), D41Market(), A1Market()));
        BuyReturns(units: 40, total: 139_480);

        await StepAsync(CommandShip(K85), Trip());

        if (gateNeedsShipParts)
        {
            await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 40, Arg.Any<CancellationToken>());
            _log.Journal.Should().NotContain(e => e.EventKind == "TradeDropped");
        }
        else
        {
            await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
            _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeDropped" && Equals(e.Properties["MinProfitPerUnit"], 200));
        }
    }

    [Fact]
    public async Task AtTheBuyMarket_ATripThatFeedsTheJumpGate_IsDropped_WhenItsGoodsWouldSellForLessThanTheyCost()
    {
        // D90: only the fuel is lost on such a trip. K85 now charges 3,488, one more than D41 pays.
        PricesAre(GateNeeds(true, K85Market(equipmentPrice: 3_488), D41Market(), A1Market()));

        var result = await StepAsync(CommandShip(K85), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
        var dropped = _log.Journal.Should().ContainSingle().Subject;
        dropped.EventKind.Should().Be("TradeDropped");
        dropped.Properties["Reason"].Should().Be("not_lucrative");
        dropped.Properties["Margin"].Should().Be(-1L);
        dropped.Properties["MinProfitPerUnit"].Should().Be(0, "a trip that feeds the gate only has to sell its goods for what they cost");
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
    public async Task AtTheSellMarket_ItSellsABatchAtATime_AndTakesTheRestElsewhere_OnceTheNextNoLongerPays()
    {
        // D79: D41 takes EQUIPMENT 20 at a time. The first 20 fetch 3,487, 233 over what they cost; after that sale D41 quotes
        // 3,420, 166 over it, and A1 pays 3,499 for 90 of fuel: the other 20 go there, with what the first fetched kept.
        SellQuotes(3_487, 3_420);

        var result = await StepAsync(Loaded(D41), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _port.Received(1).SellCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-1",
            Arg.Is<TradeBetweenMarketsGoal>(g => g.SellWaypointSymbol == A1 && g.SellWaypointChanged && g.Earned == 20 * 3_487),
            Arg.Any<CancellationToken>());
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeRerouted" && Equals(e.Properties["SellPrice"], 3_420L));
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task AtTheSellMarket_TheRestIsSoldAnyway_OnceTheNextNoLongerPays_AndNoOtherMarketPaysMore()
    {
        // Without A1 nowhere pays more: the other 20 are sold at D41 all the same.
        SellQuotes(withA1: false, 3_487, 3_420);

        var result = await StepAsync(Loaded(D41), Trip(bought: true) with { Spent = 130_160 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.Received(2).SellCargoAsync("SHIP-1", "EQUIPMENT", 20, Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync(
            "SHIP-1",
            Arg.Is<TradeBetweenMarketsGoal>(booked => booked.Earned == (20 * 3_487) + (20 * 3_420) && booked.Spent == 130_160),
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
        // It burns there (D84): A1 sells fuel, and the tank pays for the 95 twice over.
        await _bus.Received(1).InvokeAsync(Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == A1 && c.FlightMode == "BURN"), Arg.Any<CancellationToken>());
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
    public async Task AtTheSellMarket_ATripThatFeedsTheJumpGate_SellsThere_ThoughItEarnsLessThanTheMinimum()
    {
        // D90: D41 now pays 3,300, 46 a unit over what the cargo cost and under 200; A1 pays 3,499. A trip that didn't feed the
        // gate would take the cargo to A1; this one sells at D41, which makes the gate's SHIP_PARTS from it.
        PricesAre(GateNeeds(true, K85Market(), D41Market(equipmentPrice: 3_300), A1Market()));
        SellReturns(total: 132_000);

        var result = await StepAsync(Loaded(D41), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.Received(1).SellCargoAsync("SHIP-1", "EQUIPMENT", 40, Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheSellMarket_ATripThatFeedsTheJumpGate_TakesTheCargoElsewhere_WhenItWouldSellForLessThanItCost()
    {
        // D90: only the fuel is lost on such a trip. D41 now pays 3,200, under the 3,254 the cargo cost; A1 pays 3,499.
        PricesAre(GateNeeds(true, K85Market(), D41Market(equipmentPrice: 3_200), A1Market()));

        var result = await StepAsync(Loaded(D41), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _port.DidNotReceiveWithAnyArgs().SellCargoAsync(default!, default!, default, default);
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeRerouted" && Equals(e.Properties["SellWaypoint"], A1));
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

    /// <summary>The markets given, with the jump gate needing SHIP_PARTS, which D41 makes from EQUIPMENT, or nothing (D89).</summary>
    private static TradeMarketMap GateNeeds(bool shipParts, params MarketSnapshot[] markets)
        => new(Waypoints, markets, MadeFrom)
        {
            ConstructionMaterials = new HashSet<string>(shipParts ? ["SHIP_PARTS"] : [], StringComparer.OrdinalIgnoreCase),
        };

    /// <summary>The fixture, with D41 taking EQUIPMENT 20 at a time: half the command ship's hold.</summary>
    private static TradeMarketMap MapWhereD41TakesTwenty()
        => Map(
            K85Market(),
            Market(D41, Good("EQUIPMENT", "IMPORT", 7_032, 3_487, 20), Good("FUEL", "EXCHANGE", 76, 69, 180)),
            A1Market());

    /// <summary>
    /// K85 selling EQUIPMENT 20 at a time at each quote in turn: the one the arrival fetched, then the one the refresh after each
    /// purchase fetched. Each purchase costs the quote it was made at.
    /// </summary>
    private void BuyQuotes(params int[] quotes)
    {
        var purchases = 0;
        var aboard = 0;
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(_ => Context(
            Map(
                Market(K85, Good("EQUIPMENT", "EXPORT", quotes[Math.Min(purchases, quotes.Length - 1)], 1_456, 20), Good("FUEL", "EXCHANGE", 93, 79, 180)),
                D41Market(),
                A1Market()),
            Credits));
        _port.BuyCargoAsync("SHIP-1", "EQUIPMENT", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var units = call.ArgAt<int>(2);
            var cost = (long)units * quotes[Math.Min(purchases, quotes.Length - 1)];
            purchases++;
            aboard += units;
            return new TradeActionResult("AGENT", Credits - cost, new CargoModel(aboard, 40, [new CargoItemModel("EQUIPMENT", aboard)]), cost);
        });
    }

    /// <summary>D41 buying EQUIPMENT 20 at a time at each quote in turn, as <see cref="BuyQuotes"/> for sales.</summary>
    private void SellQuotes(params int[] quotes) => SellQuotes(withA1: true, quotes);

    private void SellQuotes(bool withA1, params int[] quotes)
    {
        var sales = 0;
        var aboard = 40;
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var d41 = Market(D41, Good("EQUIPMENT", "IMPORT", 7_032, quotes[Math.Min(sales, quotes.Length - 1)], 20), Good("FUEL", "EXCHANGE", 76, 69, 180));
            return Context(withA1 ? Map(K85Market(), d41, A1Market()) : Map(K85Market(), d41), Credits);
        });
        _port.SellCargoAsync("SHIP-1", "EQUIPMENT", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var units = call.ArgAt<int>(2);
            var revenue = (long)units * quotes[Math.Min(sales, quotes.Length - 1)];
            sales++;
            aboard -= units;
            return new TradeActionResult("AGENT", Credits + revenue, new CargoModel(aboard, 40, aboard > 0 ? [new CargoItemModel("EQUIPMENT", aboard)] : []), revenue);
        });
    }

    [Fact]
    public async Task ToABuyMarketAbroad_ItFliesToItsSystemsGateFirst()
    {
        // Slice 6.29 (D101): PLASTICS bought in X1-CD and sold in X1-EF. From K85 the way is the gate, 82 away, and a jump.
        AcrossSystems();

        var result = await StepAsync(CommandShip(K85), Plastics());

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == TradeAcrossFixture.AbGate),
            Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().JumpShipAsync(default!, default!, default);
    }

    [Fact]
    public async Task AtTheGate_ItJumpsTowardsTheBuyMarketAbroad()
    {
        AcrossSystems();

        var result = await StepAsync(CommandShip(TradeAcrossFixture.AbGate, "IN_ORBIT"), Plastics());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).JumpShipAsync("SHIP-1", TradeAcrossFixture.CdGate, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(Arg.Is<ShipJumpedEvent>(jumped => jumped.Cost == TradeAcrossFixture.Antimatter), Arg.Any<DeliveryOptions?>());
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "Jumped").Which.Properties["CooldownSeconds"].Should().Be(328);
        await _goals.DidNotReceiveWithAnyArgs().ClearActiveGoalAsync(default!, default);
    }

    [Fact]
    public async Task WithNoWayLeft_ATripWithNothingAboard_IsDropped()
    {
        // The gates the way went through are no longer usable: the trading plan chooses again.
        AcrossSystems();
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns((ExplorePlanState?)null);

        var result = await StepAsync(CommandShip(K85), Plastics());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        _log.Journal.Should().ContainSingle(e => e.EventKind == "TradeDropped" && Equals(e.Properties["Reason"], "no_way"));
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-1", Arg.Any<TradeBetweenMarketsGoal>(), "no_way", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoWayLeft_ATripWithItsCargoAboard_KeepsItAndWaits()
    {
        // 40 EQUIPMENT bought at K85 for X1-CD: selling them at home, or overboard (D42), would give away what they are worth
        // there; the gates give a refused way back after an hour.
        AcrossSystems();
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns((ExplorePlanState?)null);

        var result = await StepAsync(Loaded(K85), Trip(bought: true, sellAt: TradeAcrossFixture.CdMarket));

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        result.Reason.Should().Contain("waits");
        _log.Journal.Should().NotContain(e => e.EventKind == "TradeDropped" || e.EventKind == "TradeRerouted");
        await _goals.DidNotReceiveWithAnyArgs().ClearActiveGoalAsync(default!, default);
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheGate_WithTooFewCreditsToJump_ATripWithItsCargoWaitsForThem()
    {
        // D63: the antimatter, 5,000, would leave less than the floor of 60,000 of the 62,000 credits.
        AcrossSystems(credits: 62_000);

        var result = await StepAsync(Loaded(TradeAcrossFixture.AbGate, status: "IN_ORBIT"), Trip(bought: true, sellAt: TradeAcrossFixture.CdMarket));

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.DidNotReceiveWithAnyArgs().JumpShipAsync(default!, default!, default);
        await _goals.DidNotReceiveWithAnyArgs().ClearActiveGoalAsync(default!, default);
    }

    [Fact]
    public async Task AtTheBuyMarket_ATripThatSellsAbroad_KeepsBackItsAntimatterAndTheFloor()
    {
        // Of 150,000 the batches may spend what is left after the haul's fuel (170), its jump's antimatter (5,000) and the
        // floor every jump leaves (60,000): 26 at 3,254. The ship can then always jump on with its cargo (D63).
        AcrossSystems(credits: 150_000);
        BuyReturns(units: 26, total: 26 * 3_254);

        var result = await StepAsync(CommandShip(K85), Trip(sellAt: TradeAcrossFixture.CdMarket) with { Units = 26 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).BuyCargoAsync("SHIP-1", "EQUIPMENT", 26, Arg.Any<CancellationToken>());
        await _tradeContexts.Received().ReadReachAsync(SystemSymbol, Arg.Any<CancellationToken>());
    }

    /// <summary>PLASTICS bought in X1-CD and sold in X1-EF (slice 6.29).</summary>
    private static TradeBetweenMarketsGoal Plastics() => new()
    {
        TradeSymbol = "PLASTICS",
        BuyWaypointSymbol = TradeAcrossFixture.CdMarket,
        SellWaypointSymbol = TradeAcrossFixture.EfMarket,
        Units = 40,
        ExpectedProfit = 29_000,
        Jumps = 2,
    };

    /// <summary>
    /// The three systems of <see cref="TradeAcrossFixture"/>, as the trade contexts and the gates read them; the floor 60,000, and
    /// a jump to X1-CD's gate costing 5,000 and leaving a cooldown of 328 seconds.
    /// </summary>
    private void AcrossSystems(long credits = Credits)
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(TradeAcrossFixture.AcrossMap(), credits));
        _tradeContexts.ReadReachAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(TradeAcrossFixture.AcrossMap(), credits));
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, A1, credits, "COSMIC", 3));
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns(TradeAcrossFixture.Network());
        _settings.GetAsync<long>(CreditReserve.FloorSetting, Arg.Any<CancellationToken>()).Returns(TradeAcrossFixture.Floor);
        _markets.FindSnapshotByWaypointAsync(TradeAcrossFixture.AbGate, Arg.Any<CancellationToken>()).Returns(Market(
            TradeAcrossFixture.AbGate,
            Good("ANTIMATTER", "EXCHANGE", (int)TradeAcrossFixture.Antimatter, 4_800, 10),
            Good("FUEL", "EXCHANGE", 90, 80, 180)));
        _port.JumpShipAsync("SHIP-1", TradeAcrossFixture.CdGate, Arg.Any<CancellationToken>()).Returns(new JumpActionResult(
            new NavModel("IN_ORBIT", TradeAcrossFixture.Cd, TradeAcrossFixture.CdGate, "CRUISE", TradeAcrossFixture.CdGate, DateTimeOffset.UtcNow),
            328,
            DateTimeOffset.UtcNow.AddSeconds(328),
            TradeAcrossFixture.Antimatter,
            credits - TradeAcrossFixture.Antimatter));
    }

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
                new GoalJumps(_port, _ships, _agents, _markets, _refresher, _settings, _gates, new JumpRefusals(), _tradeContexts, _dock, _orbit, _refuel, _bus, Substitute.For<IGoalWarps>(), _log.For<GoalJumps>()),
                _log.For<TradeBetweenMarketsGoalExecutor>())
            .ExecuteStepAsync(ship, trip, new ShipGoalContext(), CancellationToken.None);
}
