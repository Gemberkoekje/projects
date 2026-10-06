using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>
/// Slice 6.30 (D98, D30): the command ship flies to the shipyard that sells the first explorer, in another system, and buys it
/// there. A <see cref="MoveToWaypointGoal"/> to a waypoint in another system goes through the built gates, as every flight
/// between systems does (D101): to its system's gate, then the jumps.
/// </summary>
public sealed class FetchFlightTests
{
    private const string Ship = "SPECTER-1";
    private const string Home = "X1-DC53";
    private const string HomeGate = "X1-DC53-I55";
    private const string Kr90 = "X1-KR90";
    private const string Kr90Gate = "X1-KR90-AF5F";

    private static readonly MoveToWaypointGoal Fetch = new() { TargetWaypointSymbol = "X1-KR90-YARD" };

    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IMarketRefresher _refresher = Substitute.For<IMarketRefresher>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly IRefuelSubCommand _refuel = Substitute.For<IRefuelSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IGateNetwork _gates = Substitute.For<IGateNetwork>();
    private readonly LogRecorder _log = new();

    public FetchFlightTests()
    {
        var map = new TradeMarketMap(
            [
                new WaypointCacheModel(HomeGate, Home, "JUMP_GATE", 272, -358, HasMarket: true, HasShipyard: false, DateTimeOffset.UtcNow),
                new WaypointCacheModel("X1-DC53-A1", Home, "PLANET", 250, -340, HasMarket: true, HasShipyard: false, DateTimeOffset.UtcNow),
            ],
            [],
            new Dictionary<string, IReadOnlyList<string>>());
        _tradeContexts.ReadAsync(Home, Arg.Any<CancellationToken>()).Returns(new TradeContext(map, 0, 0));
        _settings.GetAsync<long>(CreditReserve.FloorSetting, Arg.Any<CancellationToken>()).Returns(60_000L);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", "account", "X1-DC53-A1", 2_000_000, "COSMIC", 21));
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns(new ExplorePlanState
        {
            ShipSymbol = Ship,
            HomeSystemSymbol = Home,
            Status = ExploreStatus.FetchingExplorer,
            UpdatedAt = DateTimeOffset.UtcNow,
            Systems =
            [
                new KnownSystem { SystemSymbol = Home, GateWaypointSymbol = HomeGate, Gate = GateState.Active, Connections = [Kr90Gate], ExploredAt = DateTimeOffset.UtcNow },
                new KnownSystem { SystemSymbol = Kr90, GateWaypointSymbol = Kr90Gate, Gate = GateState.Active, Connections = [HomeGate], ExploredAt = DateTimeOffset.UtcNow },
            ],
        });
        _port.JumpShipAsync(Ship, Kr90Gate, Arg.Any<CancellationToken>()).Returns(new JumpActionResult(
            new NavModel("IN_ORBIT", Kr90, Kr90Gate, "CRUISE", Kr90Gate, DateTimeOffset.UtcNow),
            100,
            DateTimeOffset.UtcNow.AddSeconds(100),
            5_024,
            1_994_976));
    }

    [Fact]
    public async Task AwayFromTheGate_ItFliesToItsSystemsGate()
    {
        var result = await StepAsync(At("X1-DC53-A1"));

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == HomeGate), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheGate_ItJumps_AndTheGoalGoesOn()
    {
        var result = await StepAsync(At(HomeGate));

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.Received(1).JumpShipAsync(Ship, Kr90Gate, Arg.Any<CancellationToken>());
        await _goals.DidNotReceive().ClearActiveGoalAsync(Ship, Arg.Any<CancellationToken>());
        _log.Journal.Should().ContainSingle().Which.EventKind.Should().Be(JournalEvents.Jumped);
    }

    [Fact]
    public async Task WithNoWayThroughBuiltGates_TheGoalEnds_ForItsPlanToChooseAgain()
    {
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns((ExplorePlanState?)null);

        var result = await StepAsync(At(HomeGate));

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _goals.Received(1).ClearActiveGoalAsync(Ship, Arg.Any<CancellationToken>());
        await _port.DidNotReceive().JumpShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AJumpTheApiRefuses_BlocksTheGoal()
    {
        _port.JumpShipAsync(Ship, Kr90Gate, Arg.Any<CancellationToken>())
            .ThrowsAsync(new JumpRefusedException(Kr90Gate, 4254, "under construction", new InvalidOperationException("400")));

        var result = await StepAsync(At(HomeGate));

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
        await _goals.Received(1).BlockGoalAsync(Ship, Fetch.GoalId, GoalJumps.RefusedReason, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AtTheShipyard_TheGoalEnds()
    {
        var result = await StepAsync(new ShipModel(Ship, Kr90, "X1-KR90-YARD", "DOCKED", "CRUISE", 400, 400, ShipType: "COMMAND"));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _goals.Received(1).ClearActiveGoalAsync(Ship, Arg.Any<CancellationToken>());
    }

    private static ShipModel At(string waypoint)
        => new(Ship, Home, waypoint, "IN_ORBIT", "CRUISE", 400, 400, CargoCapacity: 40, ShipType: "COMMAND");

    private Task<GoalExecutionResult> StepAsync(ShipModel ship)
        => new MoveToWaypointGoalExecutor(
                _goals,
                _tradeContexts,
                new GoalJumps(_port, _ships, _agents, _markets, _refresher, _settings, _gates, new JumpRefusals(), _tradeContexts, _dock, _orbit, _refuel, _bus, Substitute.For<IGoalWarps>(), _log.For<GoalJumps>()),
                _dock,
                _bus,
                _log.For<MoveToWaypointGoalExecutor>())
            .ExecuteStepAsync(ship, Fetch, new ShipGoalContext(), CancellationToken.None);
}
