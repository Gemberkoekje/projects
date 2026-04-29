using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipInOrbitMineEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_SurveysBeforeExtraction_WhenMiningShipHasSurveyEquipment_AndNoActiveSurvey()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
        var navigation = Substitute.For<INavigationPlanningService>();
        var bus = Substitute.For<IMessageBus>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", "IRON_ORE", null, 0, DateTimeOffset.UtcNow, null));
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-AST", "IN_ORBIT", "CRUISE", 80, 100, CargoCurrent: 0, CargoCapacity: 40, MountSymbols: ["MOUNT_SURVEYOR_I", "MOUNT_MINING_LASER_I"]));
        surveys.GetActiveByWaypointAsync("X1-AB-AST", Arg.Any<CancellationToken>()).Returns([]);

        var handler = new ShipInOrbitMineEventHandler(assignments, ships, surveys, inOrbit, navigation, bus, NullLogger<ShipInOrbitMineEventHandler>.Instance);
        var result = await handler.HandleAsync(new ShipInOrbitEvent("SHIP-1", "X1-AB", "X1-AB-AST", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow), CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Handled());
        await inOrbit.Received(1).SurveyAsync("SHIP-1", Arg.Any<CancellationToken>());
        await inOrbit.DidNotReceive().ExtractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Siphons_WhenSiphonAssignmentAtOrigin()
    {
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
        var navigation = Substitute.For<INavigationPlanningService>();
        var bus = Substitute.For<IMessageBus>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Siphon", "X1-AB-GG", "X1-AB-MKT", "HYDROCARBON", null, 0, DateTimeOffset.UtcNow, null));
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-GG", "IN_ORBIT", "CRUISE", 80, 100, CargoCurrent: 0, CargoCapacity: 40, ShipType: "SHIP_SIPHON_DRONE", MountSymbols: ["MOUNT_GAS_SIPHON_I"], ModulesJson: "MODULE_GAS_PROCESSOR_I"));

        var handler = new ShipInOrbitMineEventHandler(assignments, ships, surveys, inOrbit, navigation, bus, NullLogger<ShipInOrbitMineEventHandler>.Instance);
        var result = await handler.HandleAsync(new ShipInOrbitEvent("SHIP-1", "X1-AB", "X1-AB-GG", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow), CancellationToken.None);

        result.Should().BeSameAs(ChainOfCommandHandlerResult.Handled());
        await inOrbit.Received(1).SiphonAsync("SHIP-1", Arg.Any<CancellationToken>());
        await inOrbit.DidNotReceive().ExtractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
