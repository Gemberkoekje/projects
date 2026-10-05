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
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using Wolverine;
using static SpaceTraders.Application.Tests.Siphoning.SiphonFixture;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.7: a siphon goal is one trip, as a mining goal. The ship siphons at the gas giant until its hold is
/// full, keeping every gas (D33), then sells the trip's gas at the market and the goal ends. Its flights carry
/// the goal, so the arrival wakes it (B17).
/// </summary>
public sealed class SiphonAndSellGoalExecutorTests
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
    private readonly LogRecorder _log = new();

    private static readonly SiphonAndSellGoal Trip = new() { TradeSymbol = "LIQUID_HYDROGEN", SourceWaypointSymbol = C38, SellWaypointSymbol = G50 };

    public SiphonAndSellGoalExecutorTests()
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<SiphonResourcesCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShipCommandResult("SHIP-5", ShipLocalStatus.InOrbit, SystemSymbol, C38, Accepted: true));
    }

    [Fact]
    public async Task AwayFromTheGasGiant_ItFliesThere_WithItsGoal()
    {
        var result = await StepAsync(SiphonDrone(), Trip);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.ShipSymbol == "SHIP-5" && command.DestinationWaypoint == C38),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATripToAMarketOutOfReach_DriftsToTheMarket_ThenSiphonsFromThereInCruise()
    {
        // D45, slice 6.10c, as for a mining trip: the drone drifts to F48, beyond its tank, and siphons at D90, 13 from it.
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(MapWithAGasGiantNearF48(), 250_000, 200));
        var far = new SiphonAndSellGoal { TradeSymbol = "LIQUID_NITROGEN", SourceWaypointSymbol = D90, SellWaypointSymbol = F48, Drifting = true };

        // D84: the fastest way to F48 burns the 39 to C40, cruises the 70 to E47 and drifts the 126 from there, rather than
        // drifting the 228 from C39. Each arrival docks the drone.
        (await StepAsync(SiphonDrone(), far)).Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == C40 && command.FlightMode == "BURN"),
            Arg.Any<CancellationToken>());
        await StepAsync(SiphonDrone(waypoint: C40) with { FuelCurrent = 2 }, far);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == E47 && command.FlightMode == "CRUISE"),
            Arg.Any<CancellationToken>());
        _log.Journal.Should().BeEmpty();

        await StepAsync(SiphonDrone(waypoint: E47) with { FuelCurrent = 10 }, far);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == F48 && command.FlightMode == "DRIFT"),
            Arg.Any<CancellationToken>());
        _log.Journal.Should().ContainSingle().Which.EventKind.Should().Be("DriftStarted");

        var atF48 = SiphonDrone(waypoint: F48) with { FlightMode = "DRIFT", FuelCurrent = 79 };
        (await StepAsync(atF48, far)).Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _goals.Received(1).SetActiveGoalAsync("SHIP-5", Arg.Is<SiphonAndSellGoal>(goal => !goal.Drifting && goal.GoalId == far.GoalId), Arg.Any<CancellationToken>());

        // D90 is 13 from F48: burning there leaves the fuel to cruise back.
        await StepAsync(atF48, far with { Drifting = false });
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == D90 && command.FlightMode == "BURN"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheGasGiant_ItSiphonsOnce_UntilTheHoldIsFull()
    {
        var result = await StepAsync(SiphonDrone(waypoint: C38, status: "IN_ORBIT"), Trip);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _bus.Received(1).InvokeAsync<ShipCommandResult>(
            Arg.Is<SiphonResourcesCommand>(command => command.ShipSymbol == "SHIP-5" && command.TradeSymbol == "LIQUID_HYDROGEN" && command.SourceWaypoint == C38),
            Arg.Any<CancellationToken>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task OnCooldown_ItWaits()
    {
        var result = await StepAsync(SiphonDrone(waypoint: C38, status: "IN_ORBIT") with { CooldownExpiresAt = DateTimeOffset.UtcNow.AddSeconds(40) }, Trip);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        await _bus.DidNotReceiveWithAnyArgs().InvokeAsync<ShipCommandResult>(default!, default, default);
    }

    [Fact]
    public async Task WithAFullHold_OfAnyGases_TheTripTurnsToSelling()
    {
        // D33: the other gases were kept, and fill the hold too.
        var full = SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("LIQUID_HYDROGEN", 6), new CargoItemModel("HYDROCARBON", 9)]);

        await StepAsync(full, Trip);

        await _goals.Received(1).SetActiveGoalAsync("SHIP-5", Arg.Is<SiphonAndSellGoal>(goal => goal.Selling), Arg.Any<CancellationToken>());
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Selling_ItFliesToTheSellMarket_ThroughARefuellingStop()
    {
        // G50 is 99 from C38, beyond a drone's 80-unit tank: it refuels at C40 on the way.
        var full = SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("LIQUID_HYDROGEN", 15)]);

        var result = await StepAsync(full, Trip with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == C40),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Selling_InOrbitAtTheMarket_ItDocks()
    {
        var result = await StepAsync(SiphonDrone(waypoint: G50, status: "IN_ORBIT", cargo: [new CargoItemModel("LIQUID_HYDROGEN", 15)]), Trip with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _dock.Received(1).ExecuteAsync("SHIP-5", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Selling_DockedAtTheMarket_ItSellsTheTripsGas_FetchesTheMarketAgain_AndTheTripEnds()
    {
        _port.SellCargoAsync("SHIP-5", "LIQUID_HYDROGEN", 6, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT", 250_330, new CargoModel(9, 15, [new CargoItemModel("HYDROCARBON", 9)]), 330));

        var result = await StepAsync(
            SiphonDrone(waypoint: G50, cargo: [new CargoItemModel("LIQUID_HYDROGEN", 6), new CargoItemModel("HYDROCARBON", 9)]),
            Trip with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.Received(1).SellCargoAsync("SHIP-5", "LIQUID_HYDROGEN", 6, Arg.Any<CancellationToken>());
        await _port.DidNotReceive().SellCargoAsync("SHIP-5", "HYDROCARBON", Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipCargoSoldEvent>(sold => sold.ShipSymbol == "SHIP-5" && sold.Units == 6 && sold.Revenue == 330 && sold.WaypointSymbol == G50),
            Arg.Any<DeliveryOptions>());
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "CargoSold");
        await _refresher.Received(1).RefreshAfterTradeAsync(SystemSymbol, G50, "SHIP-5", Arg.Any<CancellationToken>());
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-5", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASoldTrip_IsBooked_WithWhatItsSaleBroughtIn()
    {
        // D46: what each trip made after fuel, booked when it ends. The other gases are sold on trips of their own.
        _port.SellCargoAsync("SHIP-5", "LIQUID_HYDROGEN", 6, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT", 250_330, new CargoModel(9, 15, [new CargoItemModel("HYDROCARBON", 9)]), 330));

        await StepAsync(
            SiphonDrone(waypoint: G50, cargo: [new CargoItemModel("LIQUID_HYDROGEN", 6), new CargoItemModel("HYDROCARBON", 9)]),
            Trip with { Selling = true });

        await _trips.Received(1).BookAsync(
            "SHIP-5",
            Arg.Is<SiphonAndSellGoal>(booked => booked.GoalId == Trip.GoalId && booked.Earned == 330 && booked.Spent == 0),
            "sold",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoneOfTheTripsGasAboard_TheTripEnds_AndThePlanSellsTheOthers()
    {
        var result = await StepAsync(SiphonDrone(waypoint: C38, status: "IN_ORBIT", cargo: [new CargoItemModel("HYDROCARBON", 15)]), Trip with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-5", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-5", Arg.Is<SiphonAndSellGoal>(booked => booked.GoalId == Trip.GoalId), "nothing_aboard", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMarketThatNoLongerBuysTheGas_EndsTheTrip()
    {
        // C40 sells only fuel; the siphon plan then sells the gas where it fetches most.
        var result = await StepAsync(SiphonDrone(waypoint: C40, cargo: [new CargoItemModel("LIQUID_HYDROGEN", 15)]), Trip with { SellWaypointSymbol = C40, Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().SellCargoAsync(default!, default!, default, default);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-5", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-5", Arg.Is<SiphonAndSellGoal>(booked => booked.Earned == 0), "not_bought_here", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASiphonTheCommandRejects_EndsTheTrip()
    {
        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<SiphonResourcesCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(ShipCommandResult.Rejected("SHIP-5", ShipLocalStatus.InOrbit, SystemSymbol, C38));

        var result = await StepAsync(SiphonDrone(waypoint: C38, status: "IN_ORBIT"), Trip);

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-5", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-5", Arg.Is<SiphonAndSellGoal>(booked => booked.GoalId == Trip.GoalId), "rejected", Arg.Any<CancellationToken>());
    }

    private Task<GoalExecutionResult> StepAsync(ShipModel ship, SiphonAndSellGoal trip)
        => new SiphonAndSellGoalExecutor(
                _ships,
                _goals,
                _agents,
                _port,
                _tradeContexts,
                _refresher,
                _dock,
                _bus,
                _trips,
                _log.For<SiphonAndSellGoalExecutor>())
            .ExecuteStepAsync(ship, trip, new ShipGoalContext(), CancellationToken.None);
}
