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
using static SpaceTraders.Application.Tests.SpareTime.SpareTimeFixture;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.8: a spare-time trip mines or siphons at its source until the hold is full, keeping whatever sells, then
/// sells the hold one good at a time, each where it fetches most after fuel (D36), and ends. Its flights carry the goal,
/// so the arrival wakes it (B17).
/// </summary>
public sealed class GatherAndSellGoalExecutorTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IMarketRefresher _refresher = Substitute.For<IMarketRefresher>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly LogRecorder _log = new();

    private static readonly GatherAndSellGoal Mining = new() { SourceWaypointSymbol = XB5C };
    private static readonly GatherAndSellGoal Siphoning = new() { SourceWaypointSymbol = C38, Siphoning = true };

    public GatherAndSellGoalExecutorTests()
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<ExtractResourcesCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShipCommandResult("SHIP-1", ShipLocalStatus.InOrbit, SystemSymbol, XB5C, Accepted: true));
        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<SiphonResourcesCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(new ShipCommandResult("SHIP-1", ShipLocalStatus.InOrbit, SystemSymbol, C38, Accepted: true));
    }

    [Fact]
    public async Task AwayFromItsSource_ItFliesThere_WithItsGoal()
    {
        var result = await StepAsync(CommandShip(H51, "DOCKED"), Mining);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.ShipSymbol == "SHIP-1" && command.DestinationWaypoint == XB5C),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheAsteroid_ItMinesOnce_WithoutASurvey()
    {
        var result = await StepAsync(CommandShip(XB5C), Mining);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _bus.Received(1).InvokeAsync<ShipCommandResult>(
            Arg.Is<ExtractResourcesCommand>(command => command.ShipSymbol == "SHIP-1" && command.SourceWaypoint == XB5C),
            Arg.Any<CancellationToken>(),
            Arg.Any<TimeSpan?>());
        await _bus.DidNotReceive().InvokeAsync<ShipCommandResult>(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task AtTheGasGiant_ItSiphonsOnce_ForWhateverSells()
    {
        var result = await StepAsync(CommandShip(C38), Siphoning);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _bus.Received(1).InvokeAsync<ShipCommandResult>(
            Arg.Is<SiphonResourcesCommand>(command => command.ShipSymbol == "SHIP-1" && command.SourceWaypoint == C38 && command.TradeSymbol == "whatever sells"),
            Arg.Any<CancellationToken>(),
            Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task OnCooldown_ItWaits()
    {
        var result = await StepAsync(CommandShip(XB5C) with { CooldownExpiresAt = DateTimeOffset.UtcNow.AddSeconds(40) }, Mining);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        await _bus.DidNotReceiveWithAnyArgs().InvokeAsync<ShipCommandResult>(default!, default, default);
    }

    [Fact]
    public async Task InTransit_ItWaits()
    {
        var result = await StepAsync(CommandShip(H51, "IN_TRANSIT") with { DestWaypointSymbol = XB5C, ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(1) }, Mining);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenNoMarketBuysWhatItsSourceYields_TheTripEnds()
    {
        // Every extraction would go overboard: the plan chooses another place.
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(Market(XB5C, Good("FUEL", "EXCHANGE", 97, 82, 180))));

        var result = await StepAsync(CommandShip(XB5C), Mining);

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _bus.DidNotReceiveWithAnyArgs().InvokeAsync<ShipCommandResult>(default!, default, default);
    }

    [Fact]
    public async Task AnExtractionTheCommandRejects_DropsTheTrip()
    {
        _bus.InvokeAsync<ShipCommandResult>(Arg.Any<ExtractResourcesCommand>(), Arg.Any<CancellationToken>(), Arg.Any<TimeSpan?>())
            .Returns(ShipCommandResult.Rejected("SHIP-1", ShipLocalStatus.InOrbit, SystemSymbol, XB5C));

        var result = await StepAsync(CommandShip(XB5C), Mining);

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithAFullHold_TheTripTurnsToSelling()
    {
        var full = CommandShip(XB5C, cargo: [new CargoItemModel("COPPER_ORE", 25), new CargoItemModel("QUARTZ_SAND", 15)]);

        await StepAsync(full, Mining);

        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", Arg.Is<GatherAndSellGoal>(goal => goal.Selling && goal.GoalId == Mining.GoalId), Arg.Any<CancellationToken>());
        await _bus.DidNotReceiveWithAnyArgs().InvokeAsync<ShipCommandResult>(default!, default, default);
    }

    [Fact]
    public async Task Selling_ItChoosesTheGoodThatFetchesMost_RecordsTheSale_AndFliesThere()
    {
        // D36. From XB5C, 25 copper fetch 1,675 at H51, less 95 for the fuel; 15 quartz 390 at F49, less 82.
        var full = CommandShip(XB5C, cargo: [new CargoItemModel("QUARTZ_SAND", 15), new CargoItemModel("COPPER_ORE", 25)]);

        var result = await StepAsync(full, Mining with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-1",
            Arg.Is<GatherAndSellGoal>(goal => goal.SellTradeSymbol == "COPPER_ORE" && goal.SellWaypointSymbol == H51 && goal.GoalId == Mining.GoalId),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).InvokeAsync(Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == H51), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Selling_InOrbitAtTheMarket_ItDocks()
    {
        var result = await StepAsync(
            CommandShip(H51, cargo: [new CargoItemModel("COPPER_ORE", 25)]),
            Mining with { Selling = true, SellTradeSymbol = "COPPER_ORE", SellWaypointSymbol = H51 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _dock.Received(1).ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().SellCargoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Selling_DockedAtTheMarket_ItSellsTheGood_InBatchesOfTheTradeVolume_AndFetchesTheMarketAgain()
    {
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(
            Market(XB5C, Good("FUEL", "EXCHANGE", 97, 82, 180)),
            Market(H51, Good("COPPER_ORE", "IMPORT", 138, 67, 15), Good("FUEL", "EXCHANGE", 95, 80, 180))));
        _port.SellCargoAsync("SHIP-1", "COPPER_ORE", 15, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT", 251_005, new CargoModel(15, 40, [new CargoItemModel("COPPER_ORE", 5), new CargoItemModel("QUARTZ_SAND", 10)]), 1_005));
        _port.SellCargoAsync("SHIP-1", "COPPER_ORE", 5, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT", 251_340, new CargoModel(10, 40, [new CargoItemModel("QUARTZ_SAND", 10)]), 335));
        var trip = Mining with { Selling = true, SellTradeSymbol = "COPPER_ORE", SellWaypointSymbol = H51 };

        var result = await StepAsync(
            CommandShip(H51, "DOCKED", cargo: [new CargoItemModel("COPPER_ORE", 20), new CargoItemModel("QUARTZ_SAND", 10)]),
            trip);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).SellCargoAsync("SHIP-1", "COPPER_ORE", 15, Arg.Any<CancellationToken>());
        await _port.Received(1).SellCargoAsync("SHIP-1", "COPPER_ORE", 5, Arg.Any<CancellationToken>());
        await _port.DidNotReceive().SellCargoAsync("SHIP-1", "QUARTZ_SAND", Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _bus.Received(2).PublishAsync(Arg.Is<ShipCargoSoldEvent>(sold => sold.ShipSymbol == "SHIP-1" && sold.WaypointSymbol == H51), Arg.Any<DeliveryOptions>());
        _log.Journal.Where(entry => entry.EventKind == "CargoSold").Should().HaveCount(2);
        await _refresher.Received(1).RefreshAfterTradeAsync(SystemSymbol, H51, "SHIP-1", Arg.Any<CancellationToken>());

        // The next step chooses the next sale: the goal goes on, without a chosen sale.
        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-1",
            Arg.Is<GatherAndSellGoal>(goal => goal.Selling && goal.SellTradeSymbol.Length == 0 && goal.GoalId == Mining.GoalId),
            Arg.Any<CancellationToken>());
        await _goals.DidNotReceive().ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Selling_WithNothingLeftThatPaysForItsFuel_TheTripEnds_AndItStaysAboard()
    {
        // One quartz fetches 26 at F49, against 82 for the fuel there.
        var result = await StepAsync(CommandShip(H51, "DOCKED", cargo: [new CargoItemModel("QUARTZ_SAND", 1)]), Mining with { Selling = true });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _goals.Received(1).ClearActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().JettisonCargoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Selling_AFullHold_SellsEvenWhereTheSaleDoesntPayForItsFuel()
    {
        // F49's fuel costs 5,000 here: 40 quartz fetch 1,040. The next trip would have no room.
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(
            Market(XB5C, Good("FUEL", "EXCHANGE", 97, 82, 180)),
            Market(F49, Good("QUARTZ_SAND", "IMPORT", 52, 26, 60), Good("FUEL", "EXCHANGE", 5_000, 72, 180))));

        await StepAsync(CommandShip(XB5C, cargo: [new CargoItemModel("QUARTZ_SAND", 40)]), Mining with { Selling = true });

        await _goals.Received(1).SetActiveGoalAsync(
            "SHIP-1",
            Arg.Is<GatherAndSellGoal>(goal => goal.SellTradeSymbol == "QUARTZ_SAND" && goal.SellWaypointSymbol == F49),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Selling_AtAMarketThatNoLongerBuysTheGood_ItChoosesAgain()
    {
        // H51's refreshed prices list no copper any more.
        _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(
            Market(H51, Good("FUEL", "EXCHANGE", 95, 80, 180))));

        var result = await StepAsync(
            CommandShip(H51, "DOCKED", cargo: [new CargoItemModel("COPPER_ORE", 25)]),
            Mining with { Selling = true, SellTradeSymbol = "COPPER_ORE", SellWaypointSymbol = H51 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.DidNotReceiveWithAnyArgs().SellCargoAsync(default!, default!, default, default);
        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", Arg.Is<GatherAndSellGoal>(goal => goal.SellTradeSymbol.Length == 0), Arg.Any<CancellationToken>());
    }

    private Task<GoalExecutionResult> StepAsync(ShipModel ship, GatherAndSellGoal trip)
        => new GatherAndSellGoalExecutor(
                _ships,
                _goals,
                _agents,
                _port,
                _tradeContexts,
                _refresher,
                _dock,
                _bus,
                _log.For<GatherAndSellGoalExecutor>())
            .ExecuteStepAsync(ship, trip, new ShipGoalContext(), CancellationToken.None);
}
