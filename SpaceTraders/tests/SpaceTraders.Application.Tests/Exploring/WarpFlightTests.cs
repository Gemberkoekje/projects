using FluentAssertions;
using NSubstitute;
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
/// Slice 6.31 (D101: "One planner for every way ... Whether to jump or use a warp drive if the ship has one", and "Every executor
/// flies through it"): a flight to another system goes through <see cref="GoalJumps"/>, and for a ship with a warp drive its
/// step is the first of the fastest way (<see cref="IGoalWarps"/>): a warp where that is faster or the only way, a jump
/// otherwise. A ship without one jumps as before. Shown with a move to a shipyard of another system.
/// </summary>
public sealed class WarpFlightTests
{
    private const string Ship = "SPECTER-50";
    private const string Home = "X1-DC53";
    private const string HomeGate = "X1-DC53-I55";
    private const string Kr90 = "X1-KR90";
    private const string Kr90Gate = "X1-KR90-AF5F";

    private static readonly MoveToWaypointGoal Move = new() { TargetWaypointSymbol = "X1-KR90-YARD" };

    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IGateNetwork _gates = Substitute.For<IGateNetwork>();
    private readonly IGoalWarps _warps = Substitute.For<IGoalWarps>();
    private readonly LogRecorder _log = new();

    public WarpFlightTests()
    {
        _tradeContexts.ReadAsync(Home, Arg.Any<CancellationToken>()).Returns(new TradeContext(
            new TradeMarketMap(
                [
                    new WaypointCacheModel(HomeGate, Home, "JUMP_GATE", 272, -358, HasMarket: true, HasShipyard: false, DateTimeOffset.UtcNow),
                    new WaypointCacheModel("X1-DC53-A1", Home, "PLANET", 250, -340, HasMarket: true, HasShipyard: false, DateTimeOffset.UtcNow),
                ],
                [],
                new Dictionary<string, IReadOnlyList<string>>()),
            0,
            0));
        _settings.GetAsync<long>(CreditReserve.FloorSetting, Arg.Any<CancellationToken>()).Returns(60_000L);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", "account", "X1-DC53-A1", 2_000_000, "COSMIC", 21));
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns(new ExplorePlanState
        {
            ShipSymbol = "SPECTER-1",
            HomeSystemSymbol = Home,
            Status = ExploreStatus.Done,
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
        _warps.WarpAsync(Arg.Any<ShipModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new JumpStep(JumpStepOutcome.Warped, GoalExecutionResult.WaitingForArrival("Warping.")));
    }

    [Fact]
    public async Task WhereTheFastestWayWarps_TheFlightWarps()
    {
        WayIs(new WayStep(WayStepKind.Warp, "X1-DC53-A1", "X1-KR90-YARD", "CRUISE", 640, 904));

        var result = await StepAsync(Explorer("X1-DC53-A1"));

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _warps.Received(1).WarpAsync(Arg.Is<ShipModel>(ship => ship.Symbol == Ship), "X1-KR90-YARD", Arg.Any<CancellationToken>());
        await _port.DidNotReceive().JumpShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhereTheFastestWayJumps_ItJumpsFromTheStepsGate()
    {
        WayIs(new WayStep(WayStepKind.Jump, HomeGate, Kr90Gate, string.Empty, 0, 10));

        await StepAsync(Explorer(HomeGate));

        await _port.Received(1).JumpShipAsync(Ship, Kr90Gate, Arg.Any<CancellationToken>());
        await _warps.DidNotReceiveWithAnyArgs().WarpAsync(default!, default!, default);
        _log.Journal.Should().ContainSingle().Which.EventKind.Should().Be(JournalEvents.Jumped);
    }

    [Fact]
    public async Task AShipWithoutAWarpDrive_JumpsAsBefore()
    {
        await StepAsync(Explorer(HomeGate) with { ShipType = "COMMAND", ModulesJson = null });

        await _warps.DidNotReceiveWithAnyArgs().FindAsync(default!, default!, default);
        await _port.Received(1).JumpShipAsync(Ship, Kr90Gate, Arg.Any<CancellationToken>());
    }

    private void WayIs(WayStep first)
        => _warps.FindAsync(Arg.Any<ShipModel>(), "X1-KR90-YARD", Arg.Any<CancellationToken>()).Returns(new SystemWay([first], first.Seconds));

    private static ShipModel Explorer(string waypoint)
        => new(Ship, Home, waypoint, "IN_ORBIT", "CRUISE", 800, 800, CargoCapacity: 40, ShipType: "SHIP_EXPLORER", ModulesJson: WarpsTests.ExplorerModules);

    private Task<GoalExecutionResult> StepAsync(ShipModel ship)
        => new MoveToWaypointGoalExecutor(
                _goals,
                _tradeContexts,
                new GoalJumps(
                    _port,
                    Substitute.For<IShipRepository>(),
                    _agents,
                    Substitute.For<IMarketRepository>(),
                    Substitute.For<IMarketRefresher>(),
                    _settings,
                    _gates,
                    new JumpRefusals(),
                    _tradeContexts,
                    Substitute.For<IDockSubCommand>(),
                    Substitute.For<IOrbitSubCommand>(),
                    Substitute.For<IRefuelSubCommand>(),
                    _bus,
                    _warps,
                    _log.For<GoalJumps>()),
                Substitute.For<IDockSubCommand>(),
                _bus,
                _log.For<MoveToWaypointGoalExecutor>())
            .ExecuteStepAsync(ship, Move, new ShipGoalContext(), CancellationToken.None);
}
