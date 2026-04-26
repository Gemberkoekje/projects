using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipUndockedEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_UsesScoutHandler_WhenScoutAssignmentExists()
    {
        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var port = Substitute.For<ISpaceTradersPort>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto(
                "SHIP-1",
                "Scout",
                "X1-AB-009",
                null,
                null,
                null,
                0,
                DateTimeOffset.UtcNow,
                null,
                0));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        port.NavigateShipAsync("SHIP-1", "X1-AB-009", Arg.Any<CancellationToken>())
            .Returns(new NavigateActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-AB-009", DateTimeOffset.UtcNow.AddMinutes(2)),
                new FuelModel(90, 100)));

        var services = new ServiceCollection();
        services.AddSingleton(assignments);
        services.AddSingleton(ships);
        services.AddSingleton(planner);
        services.AddSingleton(port);
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedScoutEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedMineEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipUndockedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipUndockedScoutEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipMovingEvent));

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipMovingEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.OriginWaypointSymbol == "X1-AB-001" &&
                e.DestinationWaypointSymbol == "X1-AB-009"),
            Arg.Any<DeliveryOptions>());

        await planner.DidNotReceive().PlanAsync(Arg.Any<ShipModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_UsesMineHandler_WhenScoutSkips()
    {
        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var port = Substitute.For<ISpaceTradersPort>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto(
                "SHIP-1",
                "Mine",
                "X1-AB-077",
                null,
                null,
                null,
                0,
                DateTimeOffset.UtcNow,
                null,
                0));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        port.NavigateShipAsync("SHIP-1", "X1-AB-077", Arg.Any<CancellationToken>())
            .Returns(new NavigateActionResult(
                new NavModel("IN_TRANSIT", "X1-AB", "X1-AB-001", "CRUISE", "X1-AB-077", DateTimeOffset.UtcNow.AddMinutes(3)),
                new FuelModel(85, 100)));

        var services = new ServiceCollection();
        services.AddSingleton(assignments);
        services.AddSingleton(ships);
        services.AddSingleton(planner);
        services.AddSingleton(port);
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedScoutEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedMineEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipUndockedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipUndockedMineEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipMovingEvent));

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipMovingEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.OriginWaypointSymbol == "X1-AB-001" &&
                e.DestinationWaypointSymbol == "X1-AB-077"),
            Arg.Any<DeliveryOptions>());

        await planner.DidNotReceive().PlanAsync(Arg.Any<ShipModel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_UsesFallbackHandler_WhenNoRoleSpecificAssignmentMatches()
    {
        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var planner = Substitute.For<IShipAssignmentPlanner>();
        var port = Substitute.For<ISpaceTradersPort>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns((ShipAssignmentDto?)null);

        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100);
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship);
        planner.PlanAsync(ship, Arg.Any<CancellationToken>())
            .Returns(new AssignShipCommand("SHIP-1", "Scout", OriginWaypoint: "X1-AB-123"));

        var services = new ServiceCollection();
        services.AddSingleton(assignments);
        services.AddSingleton(ships);
        services.AddSingleton(planner);
        services.AddSingleton(port);
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedScoutEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedMineEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipUndockedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipUndockedEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipRoleSetEvent));

        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(dto =>
                dto.ShipSymbol == "SHIP-1" &&
                dto.AssignmentType == "Scout" &&
                dto.OriginWaypoint == "X1-AB-123"),
            Arg.Any<CancellationToken>());

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipRoleSetEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.Role == SpaceTraders.Domain.Enums.ShipRole.Explorer),
            Arg.Any<DeliveryOptions>());

        await port.DidNotReceive().NavigateShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
