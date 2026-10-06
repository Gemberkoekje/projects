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
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>
/// A jump of the command ship's exploring (asked on 2026-10-04): it flies to its system's gate and jumps to a gate that one
/// connects to, which buys one ANTIMATTER at the gate's market. Your decision of that day: a jump keeps the credit floor
/// every ship purchase keeps (<c>FleetExpansion.MinCreditReserve</c>, 60,000).
/// </summary>
public sealed class JumpGoalExecutorTests
{
    private const string Ship = "SPECTER-1";
    private const string Home = "X1-DC53";
    private const string HomeGate = "X1-DC53-I55";
    private const string Kr90Gate = "X1-KR90-AF5F";

    private static readonly JumpGoal Jump = new() { GateWaypointSymbol = HomeGate, DestinationGateWaypointSymbol = Kr90Gate };

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
    private readonly JumpRefusals _refusals = new();
    private readonly LogRecorder _log = new();

    public JumpGoalExecutorTests()
    {
        var gateMarket = new MarketSnapshot(
            HomeGate,
            Home,
            [
                new TradeGoodSnapshot("ANTIMATTER", "EXCHANGE", 4_520, 4_300, 10, "MODERATE"),
                new TradeGoodSnapshot("FUEL", "EXCHANGE", 72, 68, 100, "ABUNDANT"),
            ],
            [],
            [],
            ["ANTIMATTER", "FUEL"]);
        var map = new TradeMarketMap(
            [
                new WaypointCacheModel(HomeGate, Home, "JUMP_GATE", 272, -358, HasMarket: true, HasShipyard: false, DateTimeOffset.UtcNow),
                new WaypointCacheModel("X1-DC53-H52", Home, "MOON", 20, 0, HasMarket: true, HasShipyard: true, DateTimeOffset.UtcNow),
            ],
            [gateMarket],
            new Dictionary<string, IReadOnlyList<string>>());
        _tradeContexts.ReadAsync(Home, Arg.Any<CancellationToken>()).Returns(new TradeContext(map, 0, 0));
        _markets.FindSnapshotByWaypointAsync(HomeGate, Arg.Any<CancellationToken>()).Returns(gateMarket);
        _settings.GetAsync<long>(CreditReserve.FloorSetting, Arg.Any<CancellationToken>()).Returns(60_000L);
        Credits(200_000);
        _port.JumpShipAsync(Ship, Kr90Gate, Arg.Any<CancellationToken>()).Returns(new JumpActionResult(
            new NavModel("IN_ORBIT", "X1-KR90", Kr90Gate, "CRUISE", Kr90Gate, DateTimeOffset.UtcNow),
            64,
            DateTimeOffset.UtcNow.AddSeconds(64),
            4_520,
            195_480));
    }

    [Fact]
    public async Task AwayFromTheGate_ItFliesThere()
    {
        // The gate is 438 from H52, beyond the 400-unit tank, and H52 sells no fuel: the only way there drifts, as the flight
        // plans it now (D84), where it used to ask for CRUISE and leave the drift to the navigation's fallback.
        var result = await StepAsync(At("X1-DC53-H52", "DOCKED"));

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == HomeGate && command.FlightMode == "DRIFT"),
            Arg.Any<CancellationToken>());
        await _port.DidNotReceive().JumpShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DockedAtTheGate_ItFillsTheTank_Orbits_AndJumps()
    {
        var result = await StepAsync(At(HomeGate, "DOCKED") with { FuelCurrent = 310 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        Received.InOrder(() =>
        {
            _refuel.ExecuteAsync(Ship, false, Arg.Any<CancellationToken>());
            _orbit.ExecuteAsync(Ship, Arg.Any<CancellationToken>());
            _port.JumpShipAsync(Ship, Kr90Gate, Arg.Any<CancellationToken>());
        });
        await _ships.Received(1).UpdateNavAsync(Ship, Arg.Is<NavModel>(nav => nav.SystemSymbol == "X1-KR90" && nav.WaypointSymbol == Kr90Gate), null, Arg.Any<CancellationToken>());
        await _ships.Received(1).UpdateCooldownAsync(Ship, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(Arg.Is<ShipJumpedEvent>(jumped => jumped.Cost == 4_520 && jumped.FromWaypointSymbol == HomeGate && jumped.ToWaypointSymbol == Kr90Gate), Arg.Any<DeliveryOptions?>());
        await _goals.Received(1).ClearActiveGoalAsync(Ship, Arg.Any<CancellationToken>());
        var jumped = _log.Journal.Should().ContainSingle().Subject;
        jumped.EventKind.Should().Be(JournalEvents.Jumped);
        jumped.Message.Should().Contain(HomeGate).And.Contain(Kr90Gate).And.Contain("4520");
    }

    [Fact]
    public async Task ShouldTheAntimatterHaveRisen_ItLeavesTheJumpToThePlan()
    {
        // The plan gave the jump while it left the floor of 60,000; at the gate 64,000 less 4,520 doesn't. A goal that
        // waited here would look stuck (ShipStuck): it ends, and the plan holds the jump until the credits allow it.
        Credits(64_000);

        var result = await StepAsync(At(HomeGate, "IN_ORBIT"));

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.DidNotReceive().JumpShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _goals.Received(1).ClearActiveGoalAsync(Ship, Arg.Any<CancellationToken>());
        _log.Journal.Should().BeEmpty("the plan says why it waits");

        Credits(64_520);
        (await StepAsync(At(HomeGate, "IN_ORBIT"))).Outcome.Should().Be(GoalExecutionOutcome.Completed);
    }

    [Fact]
    public async Task ItWaitsOutTheCooldown()
    {
        var result = await StepAsync(At(HomeGate, "IN_ORBIT") with { CooldownExpiresAt = DateTimeOffset.UtcNow.AddSeconds(40) });

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        await _port.DidNotReceive().JumpShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AJumpTheApiRefuses_BlocksTheGoal_ForThePlanToChooseAgain()
    {
        _port.JumpShipAsync(Ship, Kr90Gate, Arg.Any<CancellationToken>())
            .ThrowsAsync(new JumpRefusedException(Kr90Gate, 4254, "under construction", new InvalidOperationException("400")));

        var result = await StepAsync(At(HomeGate, "IN_ORBIT"));

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
        await _goals.Received(1).BlockGoalAsync(Ship, Jump.GoalId, JumpGoalExecutor.RefusedReason, Arg.Any<CancellationToken>());
        await _ships.DidNotReceive().UpdateNavAsync(Arg.Any<string>(), Arg.Any<NavModel>(), Arg.Any<FuelModel?>(), Arg.Any<CancellationToken>());
        _log.Journal.Should().ContainSingle().Which.EventKind.Should().Be(JournalEvents.ShipBlocked);

        // Slice 6.28: every way between systems leaves that gate alone for an hour, the probes' too.
        var network = _refusals.Apply(new ExplorePlanState
        {
            ShipSymbol = Ship,
            HomeSystemSymbol = Home,
            Status = ExploreStatus.Exploring,
            UpdatedAt = DateTimeOffset.UtcNow,
            Systems =
            [
                new KnownSystem { SystemSymbol = Home, GateWaypointSymbol = HomeGate, Gate = GateState.Active, Connections = [Kr90Gate], ExploredAt = DateTimeOffset.UtcNow },
                new KnownSystem { SystemSymbol = "X1-KR90", GateWaypointSymbol = Kr90Gate, Gate = GateState.Active, ExploredAt = DateTimeOffset.UtcNow },
            ],
        });
        ExploreAtlas.TryFindJumps(network, Home, "X1-KR90", DateTimeOffset.UtcNow, out _).Should().BeFalse();
        ExploreAtlas.TryFindJumps(network, Home, "X1-KR90", DateTimeOffset.UtcNow.AddMinutes(61), out _).Should().BeTrue();
    }

    [Fact]
    public async Task InTheDestinationsSystem_TheGoalEnds()
    {
        var result = await StepAsync(new ShipModel(Ship, "X1-KR90", Kr90Gate, "IN_ORBIT", "CRUISE", 400, 400, ShipType: "COMMAND"));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _goals.Received(1).ClearActiveGoalAsync(Ship, Arg.Any<CancellationToken>());
        await _port.DidNotReceive().JumpShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static ShipModel At(string waypoint, string status)
        => new(Ship, Home, waypoint, status, "CRUISE", 400, 400, CargoCapacity: 40, ShipType: "COMMAND");

    private void Credits(long credits)
        => _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", "account", "X1-DC53-A1", credits, "COSMIC", 21));

    private Task<GoalExecutionResult> StepAsync(ShipModel ship, JumpGoal? goal = null)
        => new JumpGoalExecutor(
                _goals,
                _tradeContexts,
                new GoalJumps(_port, _ships, _agents, _markets, _refresher, _settings, _gates, _refusals, _tradeContexts, _dock, _orbit, _refuel, _bus, Substitute.For<IGoalWarps>(), _log.For<GoalJumps>()),
                _dock,
                _bus)
            .ExecuteStepAsync(ship, goal ?? Jump, new ShipGoalContext(), CancellationToken.None);
}
