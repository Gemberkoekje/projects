using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class ExtractResourcesHandlerTests
{
    [Fact]
    public async Task ExtractResources_WhenNotInOrbit_PublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100));

        var handler = new ExtractResourcesHandler(port, ships, waypoints, surveys, assignments, bus, NullLogger<ExtractResourcesHandler>.Instance);
        await handler.Handle(new ExtractResourcesCommand("SHIP-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task ExtractResources_WhenInOrbit_AtExtractableWaypoint_CallsApi()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));
        waypoints.FindAsync("X1-AB-001", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-001", "X1-AB", "ASTEROID", 0, 0, false, false, DateTimeOffset.UtcNow));
        surveys.GetBestActiveSurveyAsync("X1-AB-001", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SurveyModel(string.Empty, "X1-AB-001", [], DateTimeOffset.MinValue, string.Empty));
        port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ExtractionActionResult("IRON_ORE", 10, new CargoModel(10, 60), 60));

        var handler = new ExtractResourcesHandler(port, ships, waypoints, surveys, assignments, bus, NullLogger<ExtractResourcesHandler>.Instance);
        await handler.Handle(new ExtractResourcesCommand("SHIP-1"), CancellationToken.None);

        await port.Received(1).ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractResources_WhenSurveyAvailable_UsesSurveyExtraction()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var bus = Substitute.For<IMessageBus>();
        var survey = new SurveyModel("SIG-1", "X1-AB-001", [new SurveyDepositModel("IRON_ORE")], DateTimeOffset.UtcNow.AddMinutes(10), "LARGE");

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));
        waypoints.FindAsync("X1-AB-001", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-001", "X1-AB", "ASTEROID", 0, 0, false, false, DateTimeOffset.UtcNow));
        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-001", "X1-AB-MKT", "IRON_ORE", null, 0, DateTimeOffset.UtcNow, null));
        surveys.GetBestActiveSurveyAsync("X1-AB-001", "IRON_ORE", Arg.Any<CancellationToken>())
            .Returns(survey);
        port.ExtractWithSurveyAsync("SHIP-1", survey, Arg.Any<CancellationToken>())
            .Returns(new ExtractionActionResult("IRON_ORE", 10, new CargoModel(10, 60), 60));

        var handler = new ExtractResourcesHandler(port, ships, waypoints, surveys, assignments, bus, NullLogger<ExtractResourcesHandler>.Instance);
        await handler.Handle(new ExtractResourcesCommand("SHIP-1"), CancellationToken.None);

        await port.Received(1).ExtractWithSurveyAsync("SHIP-1", survey, Arg.Any<CancellationToken>());
        await port.DidNotReceive().ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>());
    }
}
