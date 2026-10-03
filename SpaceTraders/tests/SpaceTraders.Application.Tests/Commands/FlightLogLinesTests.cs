using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>
/// B53: a flight logged about a dozen lines at Information, five handlers in turn saying the ship had left or arrived.
/// On 2026-10-03 flights were about 80% of the bot's 2,850 lines an hour, which with twelve ships was on course to pass
/// the log budget. One flight runs here through the real handlers, from the refuel before take-off to the goal step
/// after docking; only the scheduler's wake-up is left out (it needs Postgres), and it logs at Debug.
/// </summary>
public sealed class FlightLogLinesTests
{
    private const string ShipSymbol = "SHIP-1";
    private const string SystemSymbol = "X1-AB";
    private const string Origin = "X1-AB-A1";
    private const string Destination = "X1-AB-B2";

    private static readonly DateTimeOffset ArrivesAt = new(2026, 10, 03, 09, 00, 00, TimeSpan.Zero);

    private readonly LogRecorder _log = new();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IDashboardNotifier _dashboard = Substitute.For<IDashboardNotifier>();
    private readonly IShipGoalExecutorService _goalExecutor = Substitute.For<IShipGoalExecutorService>();
    private readonly ShipGoal _goal = new ScoutWaypointGoal { TargetWaypointSymbol = Destination };

    private ShipModel _ship = new(ShipSymbol, SystemSymbol, Origin, "DOCKED", "CRUISE", 41, 80);

    public FlightLogLinesTests()
    {
        _ships.FindAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_ => _ship);
        _goals.GetActiveGoalAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_goal);
        _waypoints.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new WaypointCacheModel(call.Arg<string>(), SystemSymbol, "PLANET", 0, 0, HasMarket: true, HasShipyard: false, ArrivesAt));
        _port.RefuelShipAsync(ShipSymbol, false, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _ship = _ship with { FuelCurrent = 80 };
            return new RefuelActionResult(100_000, new FuelModel(80, 80), 86);
        });
        _port.OrbitShipAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_ => At("IN_ORBIT", Origin));
        _port.NavigateShipAsync(ShipSymbol, Destination, Arg.Any<CancellationToken>()).Returns(_ => new NavigateActionResult(Departed(), new FuelModel(41, 80)));
        _port.GetMarketAsync(SystemSymbol, Destination, Arg.Any<CancellationToken>()).Returns(new MarketDataModel(Destination, SystemSymbol, "[]", "[]", "[]", "[]"));
        _port.DockShipAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_ => At("DOCKED", Destination));
    }

    [Fact]
    public async Task AFlight_LogsItsRefuel_ThenOneLineWhenItLeaves_AndOneWhenItLands()
    {
        _goalExecutor.ExecuteAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(GoalExecutionResult.Completed($"Probe at {Destination}."));

        await FlyAsync();

        _log.Information.Select(line => line.Category).Should().Equal(
            [nameof(RefuelSubCommand), nameof(NavigateSubCommand), nameof(ShipNavigationCompletedHandler)],
            "a flight says when it leaves and when it lands, and a refuel is a purchase of its own");
        _log.Information[1].Message.Should().Contain(Origin).And.Contain(Destination);
        _log.Information[2].Message.Should().Contain(Destination).And.Contain("Completed").And.Contain($"Probe at {Destination}.");
    }

    [Fact]
    public async Task AFlightWithoutAGoalToResume_StillSaysItLanded()
    {
        await FlyAsync();

        _log.Information.Should().HaveCount(3);
        _log.Information[2].Category.Should().Be(nameof(ShipNavigationCompletedHandler));
        _log.Information[2].Message.Should().Contain(Destination);
    }

    private async Task FlyAsync()
    {
        var refuel = new RefuelSubCommand(_port, _ships, Substitute.For<IAgentRepository>(), _bus, _log.For<RefuelSubCommand>());
        var orbit = new OrbitSubCommand(_port, _ships, _markets, refuel, _log.For<OrbitSubCommand>());
        var navigate = new NavigateSubCommand(_port, _ships, _waypoints, Substitute.For<IShipEventScheduler>(), _dashboard, _bus, _log.For<NavigateSubCommand>());
        var dock = new DockSubCommand(_port, _ships, _dashboard, _log.For<DockSubCommand>());

        await new NavigateToWaypointHandler(_ships, _goals, _waypoints, orbit, navigate, refuel, _bus, _log.For<NavigateToWaypointHandler>())
            .Handle(new NavigateToWaypointCommand(ShipSymbol, Destination), CancellationToken.None);

        // At its arrival time the scheduler wakes the ship, and the handlers below run in turn.
        _ship = _ship with { Status = "IN_ORBIT", ArrivesAt = null, DestWaypointSymbol = null };
        await new ShipArrivedEventHandler(_goals, _ships, _bus, _log.For<ShipArrivedEventHandler>())
            .Handle(new ShipArrivedEvent(ShipSymbol, _goal.GoalId, ArrivesAt), CancellationToken.None);
        await new NavigateToWaypointArrivedHandler(_ships, _waypoints, _markets, Substitute.For<IShipyardRepository>(), dock, _port, _bus, _log.For<NavigateToWaypointArrivedHandler>())
            .Handle(new NavigateToWaypointArrivedCommand(ShipSymbol, Destination, _goal.GoalId), CancellationToken.None);
        await new ShipNavigationCompletedHandler(_goalExecutor, _dashboard, _log.For<ShipNavigationCompletedHandler>())
            .Handle(new ShipNavigationCompletedEvent(ShipSymbol, Destination, _goal.GoalId), CancellationToken.None);
    }

    /// <summary>The ship, as the API answers an orbit or a dock: at a waypoint, in a state.</summary>
    private NavModel At(string status, string waypoint)
    {
        _ship = _ship with { Status = status, WaypointSymbol = waypoint, ArrivesAt = null, DestWaypointSymbol = null };
        return new NavModel(status, SystemSymbol, waypoint, "CRUISE", null, null);
    }

    /// <summary>The ship, as the API answers the navigate call: in transit to the destination.</summary>
    private NavModel Departed()
    {
        _ship = _ship with { Status = "IN_TRANSIT", WaypointSymbol = Destination, ArrivesAt = ArrivesAt, DestWaypointSymbol = Destination, FuelCurrent = 41 };
        return new NavModel("IN_TRANSIT", SystemSymbol, Destination, "CRUISE", Destination, ArrivesAt);
    }

    /// <summary>Every line the handlers log, with the class that logged it.</summary>
    private sealed class LogRecorder
    {
        private readonly List<(string Category, LogLevel Level, string Message)> _lines = [];

        public IReadOnlyList<(string Category, LogLevel Level, string Message)> Information
            => [.. _lines.Where(line => line.Level >= LogLevel.Information)];

        public ILogger<T> For<T>() => new Logger<T>(_lines);

        private sealed class Logger<T>(List<(string Category, LogLevel Level, string Message)> lines) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => lines.Add((typeof(T).Name, logLevel, formatter(state, exception)));
        }
    }
}
