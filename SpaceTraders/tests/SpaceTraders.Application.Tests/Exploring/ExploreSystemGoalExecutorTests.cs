using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>
/// The command ship scouts a system it explores (asked on 2026-10-04): each market and shipyard once, as the scout plan does
/// at home. The gate it jumped to is its first stop, and a jump, unlike an arrival, stores no market.
/// </summary>
public sealed class ExploreSystemGoalExecutorTests
{
    private const string Ship = "SPECTER-1";
    private const string System = "X1-KR90";
    private const string Gate = "X1-KR90-AF5F";
    private const string Yard = "X1-KR90-YARD";
    private const string Far = "X1-KR90-FAR";

    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IMarketRefresher _refresher = Substitute.For<IMarketRefresher>();
    private readonly IWaypointVisitService _visits = Substitute.For<IWaypointVisitService>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private readonly ExploreSystemGoal _goal = new()
    {
        SystemSymbol = System,
        Stops = [Gate, Yard, Far],
        StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
    };

    public ExploreSystemGoalExecutorTests()
    {
        WaypointCacheModel[] waypoints =
        [
            new(Gate, System, "JUMP_GATE", 100, 0, HasMarket: true, HasShipyard: false, DateTimeOffset.UtcNow),
            new(Yard, System, "ORBITAL_STATION", 0, 0, HasMarket: false, HasShipyard: true, DateTimeOffset.UtcNow),
            new(Far, System, "PLANET", -300, 0, HasMarket: true, HasShipyard: false, DateTimeOffset.UtcNow),
        ];
        foreach (var waypoint in waypoints)
        {
            _waypoints.FindAsync(waypoint.Symbol, Arg.Any<CancellationToken>()).Returns(waypoint);
        }

        _tradeContexts.ReadAsync(System, Arg.Any<CancellationToken>())
            .Returns(new TradeContext(new TradeMarketMap(waypoints, [], new Dictionary<string, IReadOnlyList<string>>()), 0, 0));
        _port.GetShipyardAsync(System, Yard, Arg.Any<CancellationToken>()).Returns(new ShipyardDataModel(Yard, System, """["SHIP_PROBE"]"""));
    }

    [Fact]
    public async Task AtTheGateItJumpedTo_ItFetchesTheMarket_AndMovesOn()
    {
        var result = await StepAsync(At(Gate, "IN_ORBIT"), _goal);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _refresher.Received(1).RefreshAsync(System, Gate, Arg.Any<CancellationToken>());
        await _visits.Received(1).MarkVisitedAsync(Gate, Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(Ship, Arg.Is<ExploreSystemGoal>(goal => goal.Visited == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStopTheArrivalStored_CostsNoCall()
    {
        _shipyards.GetLastObservedAtAsync(Yard, Arg.Any<CancellationToken>()).Returns(DateTimeOffset.UtcNow);

        var result = await StepAsync(At(Yard, "DOCKED"), _goal with { Visited = 1 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _port.DidNotReceive().GetShipyardAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _refresher.DidNotReceive().RefreshAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(Ship, Arg.Is<ExploreSystemGoal>(goal => goal.Visited == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AShipyardTheArrivalDidNotStore_IsFetched()
    {
        var result = await StepAsync(At(Yard, "DOCKED"), _goal with { Visited = 1 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _shipyards.Received(1).UpsertAsync(Arg.Is<ShipyardDataModel>(yard => yard.WaypointSymbol == Yard), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BetweenStops_ItFliesToTheNext_OnceTheJumpsCooldownIsOver()
    {
        var cooling = await StepAsync(At(Gate, "IN_ORBIT") with { CooldownExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30) }, _goal with { Visited = 1 });
        var flying = await StepAsync(At(Gate, "IN_ORBIT"), _goal with { Visited = 1 });

        cooling.Outcome.Should().Be(GoalExecutionOutcome.WaitingForCooldown);
        flying.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == Yard), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterTheLastStop_TheGoalEnds()
    {
        _markets.GetLastObservedAtAsync(Far, Arg.Any<CancellationToken>()).Returns(DateTimeOffset.UtcNow);

        var result = await StepAsync(At(Far, "DOCKED"), _goal with { Visited = 2 });

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        await _goals.Received(1).ClearActiveGoalAsync(Ship, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMarketThatCanNotBeFetched_DoesNotHoldTheShipUp()
    {
        _refresher.RefreshAsync(System, Gate, Arg.Any<CancellationToken>()).Returns<bool>(_ => throw new InvalidOperationException("500"));

        var result = await StepAsync(At(Gate, "IN_ORBIT"), _goal);

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _goals.Received(1).SetActiveGoalAsync(Ship, Arg.Is<ExploreSystemGoal>(goal => goal.Visited == 1), Arg.Any<CancellationToken>());
    }

    private static ShipModel At(string waypoint, string status)
        => new(Ship, System, waypoint, status, "CRUISE", 400, 400, CargoCapacity: 40, ShipType: "COMMAND");

    private Task<GoalExecutionResult> StepAsync(ShipModel ship, ExploreSystemGoal goal)
        => new ExploreSystemGoalExecutor(
                _port,
                _goals,
                _waypoints,
                _markets,
                _shipyards,
                _refresher,
                _visits,
                _tradeContexts,
                _dock,
                _bus,
                NullLogger<ExploreSystemGoalExecutor>.Instance)
            .ExecuteStepAsync(ship, goal, new ShipGoalContext(), CancellationToken.None);
}
