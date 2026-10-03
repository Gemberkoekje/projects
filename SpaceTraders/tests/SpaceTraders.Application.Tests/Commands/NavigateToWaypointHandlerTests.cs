using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class NavigateToWaypointHandlerTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly INavigateSubCommand _navigate = Substitute.For<INavigateSubCommand>();
    private readonly IRefuelSubCommand _refuel = Substitute.For<IRefuelSubCommand>();
    private readonly IFlightModeSubCommand _flightMode = Substitute.For<IFlightModeSubCommand>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private NavigateToWaypointHandler CreateHandler() =>
        new(
            _ships,
            _goals,
            _waypoints,
            _orbit,
            _navigate,
            _refuel,
            _flightMode,
            _bus,
            NullLogger<NavigateToWaypointHandler>.Instance);

    private static ShipModel Ship(string waypoint, string status) =>
        new("SHIP-1", "X1-AB", waypoint, status, "CRUISE", 50, 100);

    [Theory]
    [InlineData("DOCKED")]
    [InlineData("IN_ORBIT")]
    public async Task Handle_DoesNothing_WhenShipIsAlreadyAtDestination(string status)
    {
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(Ship("X1-AB-DEST", status));

        await CreateHandler().Handle(new NavigateToWaypointCommand("SHIP-1", "X1-AB-DEST"), CancellationToken.None);

        await _bus.DidNotReceive().PublishAsync(Arg.Any<ShipNavigationCompletedEvent>(), Arg.Any<DeliveryOptions?>());
        await _refuel.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _orbit.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _navigate.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NavigatesWithActiveGoalId_WhenInOrbitElsewhere()
    {
        var goal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-DEST" };
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(Ship("X1-AB-HERE", "IN_ORBIT"));
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(goal);

        await CreateHandler().Handle(new NavigateToWaypointCommand("SHIP-1", "X1-AB-DEST"), CancellationToken.None);

        await _orbit.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _navigate.Received(1).ExecuteAsync("SHIP-1", "X1-AB-DEST", goal.GoalId, Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<ShipNavigationCompletedEvent>(), Arg.Any<DeliveryOptions?>());
    }

    [Fact]
    public async Task Handle_OrbitsThenNavigates_WhenDockedElsewhere()
    {
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(Ship("X1-AB-HERE", "DOCKED"), Ship("X1-AB-HERE", "IN_ORBIT"));

        await CreateHandler().Handle(new NavigateToWaypointCommand("SHIP-1", "X1-AB-DEST"), CancellationToken.None);

        Received.InOrder(() =>
        {
            _orbit.ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
            _navigate.ExecuteAsync("SHIP-1", "X1-AB-DEST", Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_SetsTheFlightModeAskedFor_InOrbit_BeforeNavigating()
    {
        // Slice 6.10c (D45): a drone drifts to a market out of its CRUISE reach. It leaves a market with a full tank (the
        // orbit refuels it), then switches to DRIFT, then flies.
        var inOrbit = Ship("X1-AB-HERE", "IN_ORBIT");
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(Ship("X1-AB-HERE", "DOCKED"), inOrbit);

        await CreateHandler().Handle(new NavigateToWaypointCommand("SHIP-1", "X1-AB-DEST") { FlightMode = "DRIFT" }, CancellationToken.None);

        Received.InOrder(() =>
        {
            _orbit.ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
            _flightMode.EnsureAsync(inOrbit, "DRIFT", Arg.Any<CancellationToken>());
            _navigate.ExecuteAsync("SHIP-1", "X1-AB-DEST", Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_WithoutAFlightMode_KeepsTheShipsOwn()
    {
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(Ship("X1-AB-HERE", "IN_ORBIT"));

        await CreateHandler().Handle(new NavigateToWaypointCommand("SHIP-1", "X1-AB-DEST"), CancellationToken.None);

        await _flightMode.DidNotReceiveWithAnyArgs().EnsureAsync(default!, default!, default);
        await _navigate.Received(1).ExecuteAsync("SHIP-1", "X1-AB-DEST", Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
