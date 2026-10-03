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
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.4: a mining goal is one trip. The ship mines at the asteroid, with the best survey there (the
/// extraction command picks it), until its hold is full, then sells at the market and the goal ends. Its
/// flights carry the goal, so the arrival wakes it (B17); a miner no longer hands out survey goals (B16).
/// </summary>
public sealed class MineAndSellGoalExecutorTests
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

    private static readonly MineAndSellGoal Trip = new() { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };

    public MineAndSellGoalExecutorTests()
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(Map(), 129_357, 200));
        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShipCommandResult("SHIP-3", ShipLocalStatus.InOrbit, SystemSymbol, XB5C, Accepted: true));
    }

    [Fact]
    public async Task AwayFromTheAsteroid_ItFliesThere_WithItsGoal()
    {
        var result = await StepAsync(Drone(), Trip);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.ShipSymbol == "SHIP-3" && command.DestinationWaypoint == XB5C),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheAsteroid_ItExtractsOnce_UntilTheHoldIsFull()
    {
        var result = await StepAsync(Drone(waypoint: XB5C, status: "IN_ORBIT"), Trip);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _bus.Received(1).InvokeAsync<ShipCommandResult>(
            Arg.Is<MineResourceVolumeCommand>(command => command.TradeSymbol == "COPPER_ORE" && command.SourceWaypoint == XB5C && command.RequiredUnitsTotal == 15),
            Arg.Any<CancellationToken>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task OnCooldown_ItWaits()
    {
        var result = await StepAsync(Drone(waypoint: XB5C, status: "IN_ORBIT") with { CooldownExpiresAt = DateTimeOffset.UtcNow.AddSeconds(40) }, Trip);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        await _bus.DidNotReceiveWithAnyArgs().InvokeAsync<ShipCommandResult>(default!, default, default);
    }

    [Fact]
    public async Task WithAFullHold_TheTripTurnsToSelling()
    {
        var full = Drone(waypoint: XB5C, status: "IN_ORBIT", cargo: [new CargoItemModel("COPPER_ORE", 15)]);

        await StepAsync(full, Trip);

        await _goals.Received(1).SetActiveGoalAsync("SHIP-3", Arg.Is<MineAndSellGoal>(goal => goal.Selling), Arg.Any<CancellationToken>());
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Selling_ItFliesToTheSellMarket()
    {
        var full = Drone(waypoint: XB5C, status: "IN_ORBIT", cargo: [new CargoItemModel("COPPER_ORE", 15)]);

        var result = await StepAsync(full, Trip with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == H51),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Selling_InOrbitAtTheMarket_ItDocks()
    {
        var result = await StepAsync(Drone(status: "IN_ORBIT", cargo: [new CargoItemModel("COPPER_ORE", 15)]), Trip with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _dock.Received(1).ExecuteAsync("SHIP-3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Selling_DockedAtTheMarket_ItSells_FetchesTheMarketAgain_AndTheTripEnds()
    {
        _port.SellCargoAsync("SHIP-3", "COPPER_ORE", 15, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT", 130_362, new CargoModel(0, 15, []), 1_005));

        var result = await StepAsync(Drone(cargo: [new CargoItemModel("COPPER_ORE", 15)]), Trip with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipCargoSoldEvent>(sold => sold.ShipSymbol == "SHIP-3" && sold.Units == 15 && sold.Revenue == 1_005 && sold.WaypointSymbol == H51),
            Arg.Any<DeliveryOptions>());
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "CargoSold");
        await _refresher.Received(1).RefreshAfterTradeAsync(SystemSymbol, H51, "SHIP-3", Arg.Any<CancellationToken>());
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASoldTrip_IsBooked_WithWhatItsSaleBroughtIn()
    {
        // D46: what each trip made after fuel, booked when it ends.
        _port.SellCargoAsync("SHIP-3", "COPPER_ORE", 15, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT", 130_362, new CargoModel(0, 15, []), 1_005));

        await StepAsync(Drone(cargo: [new CargoItemModel("COPPER_ORE", 15)]), Trip with { Selling = true });

        await _trips.Received(1).BookAsync(
            "SHIP-3",
            Arg.Is<MineAndSellGoal>(booked => booked.GoalId == Trip.GoalId && booked.Earned == 1_005 && booked.Spent == 0),
            "sold",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoneOfItsOreAboard_TheTripEnds_AndIsBooked()
    {
        var result = await StepAsync(Drone(cargo: []), Trip with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-3", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-3", Arg.Is<MineAndSellGoal>(booked => booked.GoalId == Trip.GoalId), "nothing_aboard", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMarketThatNoLongerBuysTheOre_EndsTheTrip()
    {
        // The mining plan then sells the ore where it fetches most.
        var result = await StepAsync(Drone(waypoint: F49, cargo: [new CargoItemModel("COPPER_ORE", 15)]), Trip with { SellWaypointSymbol = F49, Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _port.DidNotReceiveWithAnyArgs().SellCargoAsync(default!, default!, default, default);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-3", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-3", Arg.Is<MineAndSellGoal>(booked => booked.Earned == 0), "not_bought_here", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExtractionTheCommandRejects_EndsTheTrip()
    {
        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(ShipCommandResult.Rejected("SHIP-3", ShipLocalStatus.InOrbit, SystemSymbol, XB5C));

        var result = await StepAsync(Drone(waypoint: XB5C, status: "IN_ORBIT"), Trip);

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-3", Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-3", Arg.Is<MineAndSellGoal>(booked => booked.GoalId == Trip.GoalId), "rejected", Arg.Any<CancellationToken>());
    }

    private Task<GoalExecutionResult> StepAsync(ShipModel ship, MineAndSellGoal trip)
        => new MineAndSellGoalExecutor(
                _ships,
                _goals,
                _agents,
                _port,
                _tradeContexts,
                _refresher,
                _dock,
                _bus,
                _trips,
                _log.For<MineAndSellGoalExecutor>())
            .ExecuteStepAsync(ship, trip, new ShipGoalContext(), CancellationToken.None);
}
