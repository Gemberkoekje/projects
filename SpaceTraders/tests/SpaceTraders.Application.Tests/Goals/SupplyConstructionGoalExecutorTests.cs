using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.Goals;
using Wolverine;
using static SpaceTraders.Application.Tests.Construction.ConstructionFixture;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.6: one construction trip. The builder buys its load in batches of the market's trade volume (D81), each checked
/// with the prices its arrival or the last purchase's refresh fetched (D66) and against the credit reserve (D64), flies it to
/// the jump gate and supplies it. Supplying pays nothing: the trip books what it cost.
/// </summary>
public sealed class SupplyConstructionGoalExecutorTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IMarketRefresher _refresher = Substitute.For<IMarketRefresher>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ITripBook _trips = Substitute.For<ITripBook>();
    private readonly IBudgetPolicy _budget = Substitute.For<IBudgetPolicy>();
    private readonly IConstructionSites _sites = Substitute.For<IConstructionSites>();
    private readonly ConstructionRetries _retries = new();
    private readonly LogRecorder _log = new();

    public SupplyConstructionGoalExecutorTests()
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(Map(), 1_000_000, 200, 5_000));
        _sites.FindAsync(Gate, Arg.Any<CancellationToken>()).Returns(Site());
        _goals.GetActiveConstructionGoalsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, SupplyConstructionGoal>());

        // 300,000 on hand, the reserve 100,000 and this trip's own 168,000: it may spend 200,000.
        Budget(available: 300_000, reserved: 268_000);
        _port.BuyCargoAsync("SHIP-6", "FAB_MATS", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new TradeActionResult("AGENT", 300_000 - (call.ArgAt<int>(2) * 2_100L), new CargoModel(call.ArgAt<int>(2), 80, [new CargoItemModel("FAB_MATS", call.ArgAt<int>(2))]), call.ArgAt<int>(2) * 2_100L));
    }

    [Fact]
    public async Task ElsewhereWithoutCargo_ItFliesToTheBuyMarket_InBurn()
    {
        // D84: F49 sells fuel, and the 600 aboard pay for the 52 twice over.
        var result = await StepAsync(Hauler(), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.ShipSymbol == "SHIP-6" && c.DestinationWaypoint == F49 && c.FlightMode == "BURN"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InOrbitAtTheBuyMarket_ItDocks()
    {
        var result = await StepAsync(Hauler(waypoint: F49, status: "IN_ORBIT"), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _dock.Received(1).ExecuteAsync("SHIP-6", Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task AtTheBuyMarket_ItBuysTheLoadInOnePurchase_BookedForConstruction()
    {
        var result = await StepAsync(Hauler(waypoint: F49), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).BuyCargoAsync("SHIP-6", "FAB_MATS", 80, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(Arg.Is<CargoPurchasedEvent>(e => e.ForConstruction && e.Units == 80 && e.Cost == 168_000 && e.WaypointSymbol == F49), Arg.Any<DeliveryOptions>());
        await _refresher.Received(1).RefreshAfterTradeAsync(SystemSymbol, F49, "SHIP-6", Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-6",
            Arg.Is<SupplyConstructionGoal>(g => g.CargoBought && g.Units == 80 && g.PricePaidPerUnit == 2_100 && g.Spent == 168_000),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheBuyMarket_ItBuysOnlyWhatTheGateStillNeeds()
    {
        // Another agent supplied the gate meanwhile: 40 FAB_MATS are left, and SHIP-7 carries none of them.
        _sites.FindAsync(Gate, Arg.Any<CancellationToken>()).Returns(Site(fabMats: 1_560));

        await StepAsync(Hauler(waypoint: F49), Trip());

        await _port.Received(1).BuyCargoAsync("SHIP-6", "FAB_MATS", 40, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("LIMITED", "low_supply")]
    [InlineData("SCARCE", "low_supply")]
    public async Task AtTheBuyMarket_ItDropsTheTrip_WhenTheMarketsSupplyHasFallen(string supply, string reason)
    {
        // D66, with the prices its arrival fetched.
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>())
            .Returns(new TradeContext(Map(GateMarket(), F49Market(supply: supply), D42Market(), H51Market(), I56Market()), 1_000_000, 200, 5_000));

        var result = await StepAsync(Hauler(waypoint: F49), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-6", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-6", Arg.Any<SupplyConstructionGoal>(), reason, Arg.Any<CancellationToken>());
        _log.Journal.Should().ContainSingle(e => e.EventKind == "ConstructionDropped" && Equals(e.Properties["Reason"], reason));
    }

    [Fact]
    public async Task AtTheBuyMarket_ItBuysTheLoadInBatchesOfTheTradeVolume_EachAtThePriceQuotedThen()
    {
        // D81: F49 sells FAB_MATS 20 at a time, and each purchase raises the quote the refresh after it fetches (D25).
        Quotes((2_100, "ABUNDANT"), (2_184, "ABUNDANT"), (2_271, "HIGH"), (2_362, "HIGH"));
        Budget(available: 1_000_000, reserved: 268_000);

        var result = await StepAsync(Hauler(waypoint: F49), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(4).BuyCargoAsync("SHIP-6", "FAB_MATS", 20, Arg.Any<CancellationToken>());
        await _refresher.Received(4).RefreshAfterTradeAsync(SystemSymbol, F49, "SHIP-6", Arg.Any<CancellationToken>());
        await _bus.Received(4).PublishAsync(Arg.Is<CargoPurchasedEvent>(e => e.ForConstruction && e.Units == 20 && e.WaypointSymbol == F49), Arg.Any<DeliveryOptions>());
        const long spent = 20L * (2_100 + 2_184 + 2_271 + 2_362);
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-6",
            Arg.Is<SupplyConstructionGoal>(g => g.CargoBought && g.Units == 80 && g.Spent == spent && g.PricePaidPerUnit == spent / 80),
            Arg.Any<CancellationToken>());
        _log.Journal.Where(e => e.EventKind == "CargoBought").Should().HaveCount(4);
    }

    [Fact]
    public async Task BetweenBatches_TheTripIsStored_WithWhatItSpent_AndHoldsBackOnlyWhatIsLeftToBuy()
    {
        // A restart between batches goes on from what is aboard, with what the earlier ones cost (D57, D81).
        Quotes((2_100, "ABUNDANT"), (2_184, "ABUNDANT"));
        Budget(available: 1_000_000, reserved: 268_000);

        await StepAsync(Hauler(waypoint: F49), Trip() with { Units = 40, ReservedCredits = 84_000 });

        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-6",
            Arg.Is<SupplyConstructionGoal>(g => !g.CargoBought && g.Spent == 42_000 && g.ReservedCredits == 42_000),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterARestartBetweenBatches_ItBuysWhatIsLeft()
    {
        // Two batches were bought and stored before the restart: 40 of the 80 aboard.
        Quotes((2_271, "ABUNDANT"), (2_362, "ABUNDANT"));
        Budget(available: 1_000_000, reserved: 268_000);
        var trip = Trip() with { Spent = 85_680, ReservedCredits = 168_000 - 85_680 };

        await StepAsync(Hauler(waypoint: F49, cargo: [new CargoItemModel("FAB_MATS", 40)]), trip);

        await _port.Received(2).BuyCargoAsync("SHIP-6", "FAB_MATS", 20, Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-6",
            Arg.Is<SupplyConstructionGoal>(g => g.CargoBought && g.Units == 80 && g.Spent == 85_680 + (20L * 2_271) + (20L * 2_362)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheBuyMarket_WhenTheSupplyFallsToLimited_ItBuysNoMore_AndTakesWhatItHasToTheGate()
    {
        // D66 between batches: after two purchases F49's FAB_MATS are LIMITED. The trip goes on with 40.
        Quotes((2_100, "MODERATE"), (2_184, "MODERATE"), (2_271, "LIMITED"));
        Budget(available: 1_000_000, reserved: 268_000);

        var result = await StepAsync(Hauler(waypoint: F49), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(2).BuyCargoAsync("SHIP-6", "FAB_MATS", 20, Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync("SHIP-6", Arg.Is<SupplyConstructionGoal>(g => g.CargoBought && g.Units == 40), Arg.Any<CancellationToken>());
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
        _log.Journal.Should().NotContain(e => e.EventKind == "ConstructionDropped");
    }

    [Fact]
    public async Task AtTheBuyMarket_WhenTheCreditsRunOutPartWay_ItTakesWhatItHasToTheGate()
    {
        // D64 before each batch: the 100,000 it may spend pay for 42,000 and 43,680, not 45,420 more.
        Quotes((2_100, "ABUNDANT"), (2_184, "ABUNDANT"), (2_271, "ABUNDANT"));
        Budget(available: 200_000, reserved: 268_000);

        await StepAsync(Hauler(waypoint: F49), Trip());

        await _port.Received(2).BuyCargoAsync("SHIP-6", "FAB_MATS", 20, Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync("SHIP-6", Arg.Is<SupplyConstructionGoal>(g => g.CargoBought && g.Units == 40), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheBuyMarket_ALoadThatWouldDipIntoTheCreditReserve_IsNotBought()
    {
        // D64: 220,000 on hand, the reserve 100,000 besides this trip's own: 120,000, under the 168,000 the load costs.
        Budget(available: 220_000, reserved: 268_000);

        var result = await StepAsync(Hauler(waypoint: F49), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().BuyCargoAsync(default!, default!, default, default);
        await _trips.Received(1).BookAsync("SHIP-6", Arg.Any<SupplyConstructionGoal>(), "over_budget", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheBuyMarket_WhatOtherTripsCarry_IsNotBoughtAgain()
    {
        _sites.FindAsync(Gate, Arg.Any<CancellationToken>()).Returns(Site(fabMats: 1_500));
        _goals.GetActiveConstructionGoalsAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<string, SupplyConstructionGoal>
        {
            ["SHIP-6"] = Trip(),
            ["SHIP-7"] = Trip(bought: true) with { Units = 100 },
        });

        var result = await StepAsync(Hauler(waypoint: F49), Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _trips.Received(1).BookAsync("SHIP-6", Arg.Any<SupplyConstructionGoal>(), "not_needed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheGate_ItSuppliesWhatItCarries_AndBooksTheTrip()
    {
        var supplied = Site(fabMats: 80);
        _port.SupplyConstructionAsync(SystemSymbol, Gate, "SHIP-6", "FAB_MATS", 80, Arg.Any<CancellationToken>())
            .Returns(new SupplyConstructionActionResult(supplied, new CargoModel(0, 80, [])));
        var trip = Trip(bought: true) with { Spent = 168_000 };

        var result = await StepAsync(Loaded(Gate), trip);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _ships.Received(1).UpdateCargoAsync("SHIP-6", Arg.Is<CargoModel>(cargo => cargo.Units == 0), Arg.Any<CancellationToken>());
        await _sites.Received(1).RecordAsync(supplied, SystemSymbol, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(Arg.Is<ConstructionSuppliedEvent>(e => e.UnitsSupplied == 80 && e.WaypointSymbol == Gate && !e.IsComplete), Arg.Any<DeliveryOptions>());
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-6", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-6", trip, TripBook.Supplied, Arg.Any<CancellationToken>());

        var line = _log.Journal.Should().ContainSingle(e => e.EventKind == "ConstructionSupplied").Subject;
        (line.Properties["Fulfilled"], line.Properties["Required"]).Should().Be((80, 1_600));
    }

    [Fact]
    public async Task InOrbitAtTheGate_ItDocksFirst()
    {
        var result = await StepAsync(Loaded(Gate, status: "IN_ORBIT"), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _dock.Received(1).ExecuteAsync("SHIP-6", Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().SupplyConstructionAsync(default!, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task AtTheGate_ARefusedSupply_EndsTheTrip_AndTheMaterialIsntOfferedAgainForAWhile()
    {
        _port.SupplyConstructionAsync(SystemSymbol, Gate, "SHIP-6", "FAB_MATS", 80, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConstructionRefusedException(Gate, "FAB_MATS", ConstructionRefusedException.FulfilledErrorCode, new InvalidOperationException("4801")));

        var result = await StepAsync(Loaded(Gate), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        _retries.MayTry("SHIP-6", "FAB_MATS", DateTimeOffset.UtcNow).Should().BeFalse();
        await _sites.Received(1).FetchAsync(SystemSymbol, Gate, Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-6", Arg.Any<SupplyConstructionGoal>(), "not_needed", Arg.Any<CancellationToken>());
        _log.Journal.Should().ContainSingle(e => e.EventKind == "ConstructionDropped" && e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AtTheGate_WhenTheSiteAsLastSeenNeedsNoMore_ItAsksTheApiOnce_AndKeepsTheCargoWhenItAgrees()
    {
        _sites.FindAsync(Gate, Arg.Any<CancellationToken>()).Returns(Site(fabMats: 1_600));
        _sites.FetchAsync(SystemSymbol, Gate, Arg.Any<CancellationToken>()).Returns(Site(fabMats: 1_600));

        var result = await StepAsync(Loaded(Gate), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().SupplyConstructionAsync(default!, default!, default!, default!, default, default);
        await _trips.Received(1).BookAsync("SHIP-6", Arg.Any<SupplyConstructionGoal>(), "not_needed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNothingAboard_TheTripEnds()
    {
        var result = await StepAsync(Hauler(waypoint: Gate), Trip(bought: true));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _trips.Received(1).BookAsync("SHIP-6", Arg.Any<SupplyConstructionGoal>(), TripBook.NothingAboard, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InTransit_ItWaits()
    {
        var result = await StepAsync(Hauler() with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(3) }, Trip());

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    private static SupplyConstructionGoal Trip(bool bought = false) => new()
    {
        TradeSymbol = "FAB_MATS",
        ConstructionSiteWaypointSymbol = Gate,
        BuyWaypointSymbol = F49,
        Units = 80,
        ReservedCredits = bought ? 0 : 168_000,
        CargoBought = bought,
        PricePaidPerUnit = bought ? 2_100 : 0,
    };

    private static ShipModel Loaded(string waypoint, string status = "DOCKED")
        => Hauler(waypoint: waypoint, status: status, cargo: [new CargoItemModel("FAB_MATS", 80)]);

    /// <summary>
    /// F49 selling FAB_MATS 20 at a time at each quote in turn: the one the arrival fetched, then the one the refresh after each
    /// purchase fetched. Each purchase costs the quote it was made at.
    /// </summary>
    private void Quotes(params (int Price, string Supply)[] quotes)
    {
        var purchases = 0;
        var aboard = 0;
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var (price, supply) = quotes[Math.Min(purchases, quotes.Length - 1)];
            return new TradeContext(Map(GateMarket(), F49Market(supply: supply, tradeVolume: 20, price: price), D42Market(), H51Market(), I56Market()), 1_000_000, 200, 5_000);
        });
        _port.BuyCargoAsync("SHIP-6", "FAB_MATS", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var units = call.ArgAt<int>(2);
            var cost = (long)units * quotes[Math.Min(purchases, quotes.Length - 1)].Price;
            purchases++;
            aboard += units;
            return new TradeActionResult("AGENT", 1_000_000 - cost, new CargoModel(aboard, 80, [new CargoItemModel("FAB_MATS", aboard)]), cost);
        });
    }

    private void Budget(long available, long reserved)
        => _budget.EvaluateAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new BudgetDecision(true, available, reserved, Math.Max(0, available - reserved)));

    private Task<GoalExecutionResult> StepAsync(ShipModel ship, SupplyConstructionGoal trip)
        => new SupplyConstructionGoalExecutor(
                _ships,
                _goals,
                _agents,
                _port,
                _tradeContexts,
                _refresher,
                _dock,
                _bus,
                _trips,
                _budget,
                _sites,
                _retries,
                _log.For<SupplyConstructionGoalExecutor>())
            .ExecuteStepAsync(ship, trip, new ShipGoalContext(), CancellationToken.None);
}
