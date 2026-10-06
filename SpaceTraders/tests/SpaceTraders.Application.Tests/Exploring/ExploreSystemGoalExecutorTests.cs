using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Events;
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
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IMarketRefresher _refresher = Substitute.For<IMarketRefresher>();
    private readonly IWaypointVisitService _visits = Substitute.For<IWaypointVisitService>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly LogRecorder _log = new();

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

    [Fact]
    public async Task AtAnUnchartedStop_ItChartsIt_BooksTheReward_AndStoresTheMarketTheChartShows()
    {
        // Slice 6.30 (D99): an uncharted waypoint hides its traits, a marketplace among them. The chart shows them and pays a
        // one-off reward by their rarity, which the API gives only as the agent's credits after it.
        var (uncharted, charted) = NewPlanet();
        _waypoints.FindAsync(uncharted.Symbol, Arg.Any<CancellationToken>()).Returns(uncharted, charted);
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", "account", "X1-DC53-A1", 1_000_000, "COSMIC", 21));
        _port.CreateChartAsync(Ship, Arg.Any<CancellationToken>()).Returns(new ChartActionResult(
            new WaypointDataModel(uncharted.Symbol, System, "PLANET", 50, 50, HasMarket: true, HasShipyard: false, TraitsJson: """[{"symbol":"MARKETPLACE"}]""", ChartJson: """{"submittedBy":"SPECTER"}"""),
            1_003_105));

        var result = await StepAsync(At(uncharted.Symbol, "IN_ORBIT"), _goal with { Stops = [uncharted.Symbol, Far] });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _waypoints.Received(1).UpsertRangeAsync(
            Arg.Is<IReadOnlyList<WaypointCacheModel>>(rows => rows.Count == 1 && rows[0].Symbol == uncharted.Symbol && rows[0].HasMarket && rows[0].ChartJson != null),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<WaypointChartedEvent>(chart => chart.ShipSymbol == Ship && chart.WaypointSymbol == uncharted.Symbol && chart.Reward == 3_105),
            Arg.Any<DeliveryOptions?>());
        await _agents.Received(1).UpsertAsync(Arg.Is<AgentModel>(agent => agent.Credits == 1_003_105), Arg.Any<CancellationToken>());
        await _refresher.Received(1).RefreshAsync(System, uncharted.Symbol, Arg.Any<CancellationToken>());
        var line = _log.Journal.Should().ContainSingle().Subject;
        line.EventKind.Should().Be(JournalEvents.Charted);
        line.Message.Should().Contain(uncharted.Symbol).And.Contain("3105");
    }

    [Fact]
    public async Task AChartThatFails_FetchesTheWaypointInstead_AndTheShipMovesOn()
    {
        // Another agent may have charted it meanwhile: what anyone can see of it now is kept.
        var (uncharted, charted) = NewPlanet();
        _waypoints.FindAsync(uncharted.Symbol, Arg.Any<CancellationToken>()).Returns(uncharted, charted);
        _port.CreateChartAsync(Ship, Arg.Any<CancellationToken>()).Returns<ChartActionResult>(_ => throw new InvalidOperationException("4230 already charted"));
        _port.GetWaypointAsync(System, uncharted.Symbol, Arg.Any<CancellationToken>())
            .Returns(new WaypointDataModel(uncharted.Symbol, System, "PLANET", 50, 50, HasMarket: true, HasShipyard: false, TraitsJson: """[{"symbol":"MARKETPLACE"}]"""));

        var result = await StepAsync(At(uncharted.Symbol, "IN_ORBIT"), _goal with { Stops = [uncharted.Symbol, Far] });

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _waypoints.Received(1).UpsertRangeAsync(Arg.Is<IReadOnlyList<WaypointCacheModel>>(rows => rows[0].HasMarket), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<WaypointChartedEvent>(), Arg.Any<DeliveryOptions?>());
        await _goals.Received(1).SetActiveGoalAsync(Ship, Arg.Is<ExploreSystemGoal>(goal => goal.Visited == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AChartedStop_OrAnUnchartedAsteroid_IsNotCharted()
    {
        // D99: asteroids and gas giants hold no market or shipyard; the plan gives none as a stop, and a stop is charted
        // only when it can hold one.
        var rock = new WaypointCacheModel("X1-KR90-ROCK", System, "ASTEROID", 10, 10, HasMarket: false, HasShipyard: false, DateTimeOffset.UtcNow, """[{"symbol":"UNCHARTED"}]""");
        _waypoints.FindAsync(rock.Symbol, Arg.Any<CancellationToken>()).Returns(rock);

        await StepAsync(At(Gate, "IN_ORBIT"), _goal);
        await StepAsync(At(rock.Symbol, "IN_ORBIT"), _goal with { Stops = [rock.Symbol, Far] });

        await _port.DidNotReceive().CreateChartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A planet nobody has charted, and as the chart shows it: with a marketplace.</summary>
    private static (WaypointCacheModel Uncharted, WaypointCacheModel Charted) NewPlanet()
    {
        var uncharted = new WaypointCacheModel("X1-KR90-NEW", System, "PLANET", 50, 50, HasMarket: false, HasShipyard: false, DateTimeOffset.UtcNow, """[{"symbol":"UNCHARTED"}]""");
        return (uncharted, uncharted with { HasMarket = true, TraitsJson = """[{"symbol":"MARKETPLACE"}]""" });
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
                _agents,
                _refresher,
                _visits,
                _tradeContexts,
                _dock,
                _bus,
                _log.For<ExploreSystemGoalExecutor>())
            .ExecuteStepAsync(ship, goal, new ShipGoalContext(), CancellationToken.None);
}
