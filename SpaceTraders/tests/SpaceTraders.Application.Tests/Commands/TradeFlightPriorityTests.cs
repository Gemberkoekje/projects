using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>
/// Slice 6.33 (D115), asked on 2026-10-07: "I'd like trade ships to be prioritized in rate limiting. So if a trade ship
/// docks/undocks/jumps/navigates/buys/sells it should not have to wait for a miner or a surveyor." A flight's departure runs
/// from its goal's step, but its arrival from the scheduler's wake-up, apart from any step: both handlers mark a trade trip's
/// requests themselves (<see cref="ApiPriority"/>), so the rate limit sends them first. One flight runs here through the real
/// handlers, with no mark set around them; each call to the API notes whether it was marked.
/// </summary>
public sealed class TradeFlightPriorityTests
{
    private const string ShipSymbol = "SHIP-5";
    private const string SystemSymbol = "X1-AB";
    private const string Origin = "X1-AB-A1";
    private const string Destination = "X1-AB-B2";

    private static readonly DateTimeOffset ArrivesAt = new(2026, 10, 07, 09, 00, 00, TimeSpan.Zero);

    private readonly LogRecorder _log = new();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IDashboardNotifier _dashboard = Substitute.For<IDashboardNotifier>();
    private readonly List<(string Call, bool Marked)> _calls = [];

    private ShipModel _ship = new(ShipSymbol, SystemSymbol, Origin, "DOCKED", "CRUISE", 41, 80);

    public TradeFlightPriorityTests()
    {
        _ships.FindAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_ => _ship);
        _waypoints.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new WaypointCacheModel(call.Arg<string>(), SystemSymbol, "PLANET", 0, 0, HasMarket: true, HasShipyard: false, ArrivesAt));
        _port.RefuelShipAsync(ShipSymbol, false, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Note("refuel");
            _ship = _ship with { FuelCurrent = 80 };
            return new RefuelActionResult(100_000, new FuelModel(80, 80), 86);
        });
        _port.OrbitShipAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Note("orbit");
            return At("IN_ORBIT", Origin);
        });
        _port.NavigateShipAsync(ShipSymbol, Destination, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Note("navigate");
            _ship = _ship with { Status = "IN_TRANSIT", WaypointSymbol = Destination, ArrivesAt = ArrivesAt, DestWaypointSymbol = Destination, FuelCurrent = 41 };
            return new NavigateActionResult(new NavModel("IN_TRANSIT", SystemSymbol, Destination, "CRUISE", Destination, ArrivesAt), new FuelModel(41, 80));
        });
        _port.GetMarketAsync(SystemSymbol, Destination, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Note("market");
            return new MarketDataModel(Destination, SystemSymbol, "[]", "[]", "[]", "[]");
        });
        _port.DockShipAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Note("dock");
            return At("DOCKED", Destination);
        });
    }

    [Fact]
    public async Task ATradeTripsFlight_MarksEveryCall_FromTheRefuelBeforeItLeaves_ToTheDockWhereItLands()
    {
        await FlyAsync(new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = Origin, SellWaypointSymbol = Destination });

        _calls.Select(call => call.Call).Should().Equal("refuel", "orbit", "navigate", "market", "dock");
        _calls.Should().OnlyContain(call => call.Marked);
        ApiPriority.IsTradeTrip.Should().BeFalse("the mark ends with each handler");
    }

    [Fact]
    public async Task AMiningTripsFlight_MarksNone()
    {
        await FlyAsync(new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = Origin, SellWaypointSymbol = Destination });

        _calls.Should().HaveCount(5).And.OnlyContain(call => !call.Marked);
    }

    private async Task FlyAsync(ShipGoal goal)
    {
        _goals.GetActiveGoalAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(goal);
        var refuel = new RefuelSubCommand(_port, _ships, Substitute.For<IAgentRepository>(), _bus, _log.For<RefuelSubCommand>());
        var orbit = new OrbitSubCommand(_port, _ships, _markets, refuel, _log.For<OrbitSubCommand>());
        var navigate = new NavigateSubCommand(_port, _ships, _waypoints, Substitute.For<IShipEventScheduler>(), _dashboard, _bus, _log.For<NavigateSubCommand>());
        var dock = new DockSubCommand(_port, _ships, _dashboard, _log.For<DockSubCommand>());
        var flightMode = new FlightModeSubCommand(_port, _ships, _log.For<FlightModeSubCommand>());

        await new NavigateToWaypointHandler(_ships, _goals, _waypoints, orbit, navigate, refuel, flightMode, _bus, _log.For<NavigateToWaypointHandler>())
            .Handle(new NavigateToWaypointCommand(ShipSymbol, Destination) { FlightMode = "CRUISE" }, CancellationToken.None);

        // At its arrival time the scheduler wakes the ship: no goal step is running.
        _ship = _ship with { Status = "IN_ORBIT", ArrivesAt = null, DestWaypointSymbol = null };
        await new NavigateToWaypointArrivedHandler(
                _ships,
                _goals,
                _waypoints,
                new MarketRefresher(_port, _markets, _bus, _log.For<MarketRefresher>()),
                Substitute.For<IShipyardRepository>(),
                dock,
                _port,
                _bus,
                _log.For<NavigateToWaypointArrivedHandler>())
            .Handle(new NavigateToWaypointArrivedCommand(ShipSymbol, Destination, goal.GoalId), CancellationToken.None);
    }

    private void Note(string call) => _calls.Add((call, ApiPriority.IsTradeTrip));

    /// <summary>The ship, as the API answers an orbit or a dock: at a waypoint, in a state.</summary>
    private NavModel At(string status, string waypoint)
    {
        _ship = _ship with { Status = status, WaypointSymbol = waypoint, ArrivesAt = null, DestWaypointSymbol = null };
        return new NavModel(status, SystemSymbol, waypoint, "CRUISE", null, null);
    }
}
