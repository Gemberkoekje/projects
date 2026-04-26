using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
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
        var inOrbitCommands = Substitute.For<IInOrbitCommandAcceptor>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SpaceTraders.Application.DTOs.ShipAssignmentDto(
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

        var services = new ServiceCollection();
        services.AddSingleton(assignments);
        services.AddSingleton(ships);
        services.AddSingleton(inOrbitCommands);
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

        await inOrbitCommands.Received(1).NavigateAsync("SHIP-1", "X1-AB-009", Arg.Any<CancellationToken>());

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipMovingEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.OriginWaypointSymbol == "X1-AB-001" &&
                e.DestinationWaypointSymbol == "X1-AB-009"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DispatchAsync_UsesMineHandler_WhenScoutSkips()
    {
        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var inOrbitCommands = Substitute.For<IInOrbitCommandAcceptor>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SpaceTraders.Application.DTOs.ShipAssignmentDto(
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

        var services = new ServiceCollection();
        services.AddSingleton(assignments);
        services.AddSingleton(ships);
        services.AddSingleton(inOrbitCommands);
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

        await inOrbitCommands.Received(1).NavigateAsync("SHIP-1", "X1-AB-077", Arg.Any<CancellationToken>());

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipMovingEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.OriginWaypointSymbol == "X1-AB-001" &&
                e.DestinationWaypointSymbol == "X1-AB-077"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DispatchAsync_UsesFallbackHandler_WhenNoRoleSpecificAssignmentMatches()
    {
        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var inOrbitCommands = Substitute.For<IInOrbitCommandAcceptor>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns((SpaceTraders.Application.DTOs.ShipAssignmentDto?)null);

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var services = new ServiceCollection();
        services.AddSingleton(assignments);
        services.AddSingleton(ships);
        services.AddSingleton(inOrbitCommands);
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
        result.NextEventType.Should().Be(nameof(ShipIdleDockedEvent));

        await inOrbitCommands.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipIdleDockedEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.SystemSymbol == "X1-AB" &&
                e.WaypointSymbol == "X1-AB-001"),
            Arg.Any<DeliveryOptions>());
    }
}
