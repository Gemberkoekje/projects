using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Adapters;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>
/// B83: on 2026-10-18 at 06:45:45Z SPECTER-226's navigate from X1-QK87-X45A to X1-QK87-XF4Z lost its answer (the connection
/// was cancelled), though the API had carried it out. The bot kept the ship in orbit at X45A, so every step sent the same
/// navigate again: 16 times the API answered that the ship was in transit, then, once it had landed, 200 times over six
/// hours that it was already at the destination, until the server reset. The flights here run through the real handler,
/// navigate subcommand and port adapter; only the API client is a stand-in.
/// </summary>
public sealed class LostNavigateAnswerTests
{
    private const string ShipSymbol = "SPECTER-226";
    private const string SystemSymbol = "X1-QK87";
    private const string Origin = "X1-QK87-X45A";
    private const string Destination = "X1-QK87-XF4Z";

    private static readonly DateTimeOffset Landed = new(2026, 10, 18, 06, 53, 00, TimeSpan.Zero);

    private readonly ISpaceTradersApiClient _client = Substitute.For<ISpaceTradersApiClient>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipEventScheduler _scheduler = Substitute.For<IShipEventScheduler>();
    private readonly ShipGoal _goal = new ScoutWaypointGoal { TargetWaypointSymbol = Destination };

    // As the bot stored it after the lost answer: still in orbit where it left from.
    private ShipModel _ship = new(ShipSymbol, SystemSymbol, Origin, "IN_ORBIT", "CRUISE", 300, 300);

    public LostNavigateAnswerTests()
    {
        _ships.FindAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_ => _ship);
        _ships.UpdateNavAsync(ShipSymbol, Arg.Any<NavModel>(), Arg.Any<FuelModel?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var nav = call.Arg<NavModel>();
                _ship = _ship with { Status = nav.Status, WaypointSymbol = nav.WaypointSymbol, ArrivesAt = nav.ArrivesAt, DestWaypointSymbol = nav.DestWaypointSymbol };
                return Task.CompletedTask;
            });
        _goals.GetActiveGoalAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(_goal);
    }

    [Fact]
    public async Task AShipTheApiSaysIsAlreadyThere_IsStoredThere_AndItsArrivalRuns_SoTheNextStepDoesntNavigateAgain()
    {
        Refuses(4204, $"Navigate request failed. Ship {ShipSymbol} is currently located at the destination.");
        AnswersTheShip("IN_ORBIT", Landed);

        // Two goal steps, as two ticks would take them.
        var tick = () => NavigateAsync();
        await tick.Should().NotThrowAsync("the API's answer says where the ship is");
        await tick.Should().NotThrowAsync();

        await _client.Received(1).NavigateShipAsync(ShipSymbol, Destination, Arg.Any<CancellationToken>());
        _ship.Should().BeEquivalentTo(new { Status = "IN_ORBIT", WaypointSymbol = Destination });
        await _scheduler.Received(1).ScheduleArrivalAsync(ShipSymbol, _goal.GoalId, Landed, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(4200)]
    [InlineData(4214)]
    public async Task AShipTheApiSaysIsInTransit_IsStoredInTransit_WithItsArrivalScheduled(int errorCode)
    {
        Refuses(errorCode, $"Ship is currently in-transit from {Origin} to {Destination} and arrives in 412 seconds.");
        AnswersTheShip("IN_TRANSIT", Landed);

        var tick = () => NavigateAsync();
        await tick.Should().NotThrowAsync();

        _ship.Should().BeEquivalentTo(new { Status = "IN_TRANSIT", WaypointSymbol = Destination, ArrivesAt = (DateTimeOffset?)Landed });
        await _scheduler.Received(1).ScheduleArrivalAsync(ShipSymbol, _goal.GoalId, Landed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANavigateRefusedForItsFuel_IsNotTakenForAShipElsewhere()
    {
        // The fuel fallback reads this refusal by its message (B62); it stays the API's error.
        Refuses(4203, "Navigate request failed. Ship requires 120 more fuel for navigation.");

        var navigate = () => new SpaceTradersPortAdapter(_client).NavigateShipAsync(ShipSymbol, Destination);

        (await navigate.Should().ThrowAsync<SpaceTradersApiException>()).Which.Message.Should().Contain("fuel for navigation");
        await _client.DidNotReceive().GetMyShipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private Task NavigateAsync()
    {
        var port = new SpaceTradersPortAdapter(_client);
        var waypoints = Substitute.For<IWaypointRepository>();
        var bus = Substitute.For<IMessageBus>();
        var navigate = new NavigateSubCommand(port, _ships, waypoints, _scheduler, Substitute.For<IDashboardNotifier>(), bus, NullLogger<NavigateSubCommand>.Instance);

        return new NavigateToWaypointHandler(
                _ships,
                _goals,
                waypoints,
                Substitute.For<IOrbitSubCommand>(),
                navigate,
                Substitute.For<IRefuelSubCommand>(),
                Substitute.For<IFlightModeSubCommand>(),
                bus,
                NullLogger<NavigateToWaypointHandler>.Instance)
            .Handle(new NavigateToWaypointCommand(ShipSymbol, Destination) { FlightMode = "CRUISE" }, CancellationToken.None);
    }

    /// <summary>The API's 400 for the navigate, as the client raises it.</summary>
    private void Refuses(int errorCode, string apiMessage) =>
        _client.NavigateShipAsync(ShipSymbol, Destination, Arg.Any<CancellationToken>())
            .ThrowsAsync(new SpaceTradersApiException(
                $"SpaceTraders API request to 'my/ships/{ShipSymbol}/navigate' failed with status code 400: {apiMessage}",
                HttpStatusCode.BadRequest,
                $"my/ships/{ShipSymbol}/navigate",
                "{}",
                errorCode));

    /// <summary>The ship as <c>GET my/ships/{symbol}</c> answers: at, or flying to, the destination.</summary>
    private void AnswersTheShip(string status, DateTimeOffset arrival) =>
        _client.GetMyShipAsync(ShipSymbol, Arg.Any<CancellationToken>()).Returns(new Ship
        {
            Symbol = ShipSymbol,
            Nav = new ShipNav
            {
                SystemSymbol = SystemSymbol,
                WaypointSymbol = Destination,
                Status = status,
                FlightMode = "CRUISE",
                Route = new ShipNavRoute
                {
                    Origin = new ShipNavRouteWaypoint { Symbol = Origin, SystemSymbol = SystemSymbol },
                    Destination = new ShipNavRouteWaypoint { Symbol = Destination, SystemSymbol = SystemSymbol },
                    DepartureTime = new DateTimeOffset(2026, 10, 18, 06, 45, 45, TimeSpan.Zero),
                    Arrival = arrival,
                },
            },
            Fuel = new ShipFuel { Current = 268, Capacity = 300 },
        });
}
