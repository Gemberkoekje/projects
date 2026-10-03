using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.SpareTime.SpareTimeFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.8, D34, D37: a survey or a trade takes a ship off its spare-time trip while the trip fills its hold. That
/// ends the trip, so it is booked then (D46): it has sold nothing yet, so it books the fuel it took.
/// </summary>
public sealed class SpareTimeInterruptionTests
{
    private static readonly SurveyWaypointGoal Survey = new() { TargetWaypointSymbol = XB5C, TargetDepositSymbol = "COPPER_ORE" };

    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly ITripBook _trips = Substitute.For<ITripBook>();
    private readonly LogRecorder _log = new();

    [Fact]
    public async Task AnInterruptedTrip_IsBooked()
    {
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C };
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(trip);
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(CommandShip(cargo: [new CargoItemModel("COPPER_ORE", 12)]));

        var replaced = await Interruption().TryReplaceAsync("SHIP-1", Survey, "survey", CancellationToken.None);

        replaced.Should().BeTrue();
        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", Survey, Arg.Any<CancellationToken>());
        await _trips.Received(1).BookAsync("SHIP-1", trip, "interrupted", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATripThatSells_IsNotInterrupted_NorBooked()
    {
        // Its executor books it when it ends.
        _goals.GetActiveGoalAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(new GatherAndSellGoal { SourceWaypointSymbol = XB5C, Selling = true });
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(CommandShip(H51, "DOCKED"));

        var replaced = await Interruption().TryReplaceAsync("SHIP-1", Survey, "survey", CancellationToken.None);

        replaced.Should().BeFalse();
        await _trips.DidNotReceiveWithAnyArgs().BookAsync(default!, default!, default!, default);
    }

    private SpareTimeInterruption Interruption() => new(_ships, _goals, new ShipGoalStepGuard(), _trips, _log.For<SpareTimeInterruption>());
}
