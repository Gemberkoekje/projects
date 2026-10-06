using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Goals;

/// <summary>
/// Slice 6.3: one flight of a probe per goal, in CRUISE; the arrival fetches the market. Slice 6.28 (D101): a flight to a
/// market of another system flies to its system's gate and jumps through the built gates on the way, each jump's antimatter
/// paid while the credits after it stay at the floor (D63). Home X1-AB's gate connects to X1-CD's, and X1-CD's to X1-EF's.
/// </summary>
public sealed class DeployProbeGoalExecutorTests
{
    private const string Probe1 = "PROBE-1";
    private const string HomeGate = "X1-AB-G";
    private const string CdGate = "X1-CD-G";
    private const string EfGate = "X1-EF-G";
    private const string CdMarket = "X1-CD-MKT";
    private const string EfMarket = "X1-EF-MKT";

    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IGateNetwork _gates = Substitute.For<IGateNetwork>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly JumpRefusals _refusals = new();
    private readonly LogRecorder _log = new();

    public DeployProbeGoalExecutorTests()
    {
        var now = DateTimeOffset.UtcNow;
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns(new ExplorePlanState
        {
            ShipSymbol = "SHIP-1",
            HomeSystemSymbol = "X1-AB",
            Status = ExploreStatus.Exploring,
            UpdatedAt = now,
            Systems =
            [
                new KnownSystem { SystemSymbol = "X1-AB", GateWaypointSymbol = HomeGate, Gate = GateState.Active, Connections = [CdGate], ExploredAt = now },
                new KnownSystem { SystemSymbol = "X1-CD", GateWaypointSymbol = CdGate, Gate = GateState.Active, Connections = [HomeGate, EfGate], ExploredAt = now },
                new KnownSystem { SystemSymbol = "X1-EF", GateWaypointSymbol = EfGate, Gate = GateState.Active, Connections = [CdGate], ExploredAt = now },
            ],
        });
        _tradeContexts.ReadAsync("X1-AB", Arg.Any<CancellationToken>()).Returns(new TradeContext(
            new TradeMarketMap(
                [
                    new WaypointCacheModel("X1-AB-HQ", "X1-AB", "PLANET", 0, 0, HasMarket: true, HasShipyard: false, DateTimeOffset.UnixEpoch),
                    new WaypointCacheModel(HomeGate, "X1-AB", "JUMP_GATE", 300, 0, HasMarket: true, HasShipyard: false, DateTimeOffset.UnixEpoch),
                ],
                [],
                new Dictionary<string, IReadOnlyList<string>>()),
            0,
            0));
        _markets.FindSnapshotByWaypointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new MarketSnapshot(call.Arg<string>(), "X1", [new TradeGoodSnapshot("ANTIMATTER", "EXCHANGE", 5_024, 4_800, 10, "MODERATE")], [], [], ["ANTIMATTER"]));
        _settings.GetAsync<long>(CreditReserve.FloorSetting, Arg.Any<CancellationToken>()).Returns(60_000L);
        Credits(2_000_000);
        _port.JumpShipAsync(Probe1, CdGate, Arg.Any<CancellationToken>()).Returns(new JumpActionResult(
            new NavModel("IN_ORBIT", "X1-CD", CdGate, "CRUISE", CdGate, DateTimeOffset.UtcNow),
            420,
            DateTimeOffset.UtcNow.AddSeconds(420),
            5_024,
            1_994_976));
    }

    [Theory]
    [InlineData("DOCKED")]
    [InlineData("IN_ORBIT")]
    public async Task AtItsMarket_TheFlightIsOver(string status)
    {
        // The arrival already fetched the market and docked; the probe plan chooses the next market.
        var result = await StepAsync(Probe("X1-AB-MKT", status));

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _goals.Received(1).ClearActiveGoalAsync(Probe1, Arg.Any<CancellationToken>());
        _bus.ReceivedCalls().Should().BeEmpty("the old plan set DRIFT here, which made the next flight ten times slower");
    }

    [Fact]
    public async Task Elsewhere_ItFliesThere()
    {
        var result = await StepAsync(Probe("X1-AB-HQ", "DOCKED"));

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.ShipSymbol == Probe1 && c.DestinationWaypoint == "X1-AB-MKT"),
            Arg.Any<CancellationToken>());
        await _goals.DidNotReceiveWithAnyArgs().ClearActiveGoalAsync(default!, default);
    }

    [Fact]
    public async Task AProbeInDrift_SwitchesToCruise_BeforeItFlies()
    {
        // A probe has no tank, so CRUISE costs it nothing; the old plan parked probes in DRIFT (B47).
        var result = await StepAsync(Probe("X1-AB-HQ", "DOCKED") with { FlightMode = "DRIFT" });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<PatchShipNavCommand>(c => c.ShipSymbol == Probe1 && c.FlightMode == "CRUISE"),
            Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<NavigateToWaypointCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InFlight_ItWaits()
    {
        var result = await StepAsync(Probe("X1-AB-MKT", "IN_TRANSIT") with { ArrivesAt = DateTimeOffset.MaxValue });

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        _bus.ReceivedCalls().Should().BeEmpty();
        await _goals.DidNotReceiveWithAnyArgs().ClearActiveGoalAsync(default!, default);
    }

    [Fact]
    public async Task ToAMarketAbroad_ItFliesToItsSystemsGateFirst()
    {
        // Slice 6.28: before the change the probe asked the API to navigate straight to a waypoint of another system.
        var result = await StepAsync(Probe("X1-AB-HQ", "DOCKED"), CdMarket);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(
            Arg.Is<NavigateToWaypointCommand>(c => c.ShipSymbol == Probe1 && c.DestinationWaypoint == HomeGate && c.FlightMode == "CRUISE"),
            Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().JumpShipAsync(default!, default!, default);
    }

    [Fact]
    public async Task AtTheGate_ItJumps_AndTheFlightGoesOnInTheNextSystem()
    {
        var result = await StepAsync(Probe(HomeGate, "DOCKED"), CdMarket);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        Received.InOrder(() =>
        {
            _orbit.ExecuteAsync(Probe1, Arg.Any<CancellationToken>());
            _port.JumpShipAsync(Probe1, CdGate, Arg.Any<CancellationToken>());
        });
        await _ships.Received(1).UpdateNavAsync(Probe1, Arg.Is<NavModel>(nav => nav.SystemSymbol == "X1-CD" && nav.WaypointSymbol == CdGate), null, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(Arg.Is<ShipJumpedEvent>(jumped => jumped.Cost == 5_024 && jumped.ToWaypointSymbol == CdGate), Arg.Any<DeliveryOptions?>());
        await _goals.DidNotReceiveWithAnyArgs().ClearActiveGoalAsync(default!, default);
        _log.Journal.Should().ContainSingle().Which.Message.Should().Be(
            "Jumped: ship PROBE-1 jumped from X1-AB-G to X1-CD-G in X1-CD; the antimatter cost 5024, the cooldown is 420 seconds.");

        // In the market's system, it flies on to the market.
        (await StepAsync(Probe(CdGate, "IN_ORBIT") with { SystemSymbol = "X1-CD" }, CdMarket)).Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(Arg.Is<NavigateToWaypointCommand>(c => c.DestinationWaypoint == CdMarket), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BetweenTwoJumps_ItWaitsOutTheCooldownAtTheGate()
    {
        var result = await StepAsync(Probe(CdGate, "IN_ORBIT") with { SystemSymbol = "X1-CD", CooldownExpiresAt = DateTimeOffset.UtcNow.AddMinutes(7) }, EfMarket);

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        await _port.DidNotReceiveWithAnyArgs().JumpShipAsync(default!, default!, default);
        await _goals.DidNotReceiveWithAnyArgs().ClearActiveGoalAsync(default!, default);
    }

    [Fact]
    public async Task WhereNoWayIsKnown_TheFlightEnds_ForThePlanToChooseAgain()
    {
        // A goal that waited would look stuck (ShipStuck); the plan sends no probe where no way is known.
        var result = await StepAsync(Probe(HomeGate, "IN_ORBIT"), "X1-ZZ-MKT");

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _goals.Received(1).ClearActiveGoalAsync(Probe1, Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().JumpShipAsync(default!, default!, default);
    }

    [Fact]
    public async Task AJumpTheCreditsDontAllow_EndsTheFlight()
    {
        // D63: the jump must leave the floor of 60,000; 65,000 less 5,024 doesn't.
        Credits(65_000);

        var result = await StepAsync(Probe(HomeGate, "IN_ORBIT"), CdMarket);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _goals.Received(1).ClearActiveGoalAsync(Probe1, Arg.Any<CancellationToken>());
        await _port.DidNotReceiveWithAnyArgs().JumpShipAsync(default!, default!, default);
    }

    [Fact]
    public async Task AJumpTheApiRefuses_BlocksTheFlight_AndNoWayGoesThroughThatGateForAnHour()
    {
        _port.JumpShipAsync(Probe1, CdGate, Arg.Any<CancellationToken>())
            .ThrowsAsync(new JumpRefusedException(CdGate, 4254, "under construction", new InvalidOperationException("400")));
        var goal = new DeployProbeGoal { TargetWaypointSymbol = CdMarket };

        var result = await new DeployProbeGoalExecutor(_goals, Jumps(), _bus, NullLogger<DeployProbeGoalExecutor>.Instance)
            .ExecuteStepAsync(Probe(HomeGate, "IN_ORBIT"), goal, new ShipGoalContext(), CancellationToken.None);

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
        await _goals.Received(1).BlockGoalAsync(Probe1, goal.GoalId, GoalJumps.RefusedReason, Arg.Any<CancellationToken>());
        var network = _refusals.Apply((await _gates.ReadAsync(CancellationToken.None))!);
        ExploreAtlas.TryFindJumps(network, "X1-AB", "X1-CD", DateTimeOffset.UtcNow, out _).Should().BeFalse();
    }

    private static ShipModel Probe(string waypoint, string status) =>
        new(Probe1, "X1-AB", waypoint, status, "CRUISE", 0, 0, ShipType: "SATELLITE");

    private void Credits(long credits)
        => _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, "X1-AB-HQ", credits, "COSMIC", 3));

    private GoalJumps Jumps()
        => new(
            _port,
            _ships,
            _agents,
            _markets,
            Substitute.For<IMarketRefresher>(),
            _settings,
            _gates,
            _refusals,
            _tradeContexts,
            Substitute.For<IDockSubCommand>(),
            _orbit,
            Substitute.For<IRefuelSubCommand>(),
            _bus,
            Substitute.For<IGoalWarps>(),
            _log.For<GoalJumps>());

    private Task<GoalExecutionResult> StepAsync(ShipModel probe, string target = "X1-AB-MKT")
        => new DeployProbeGoalExecutor(_goals, Jumps(), _bus, NullLogger<DeployProbeGoalExecutor>.Instance)
            .ExecuteStepAsync(probe, new DeployProbeGoal { TargetWaypointSymbol = target }, new ShipGoalContext(), CancellationToken.None);
}
