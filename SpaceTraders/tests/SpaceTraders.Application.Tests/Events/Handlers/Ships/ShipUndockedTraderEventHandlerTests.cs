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

public sealed class ShipUndockedTraderEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_UsesTraderHandler_NavigatesToBuyWaypoint_WhenShipHasNoCargo()
    {
        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var inOrbit = Substitute.For<IInOrbitCommandAcceptor>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SpaceTraders.Application.DTOs.ShipAssignmentDto(
                "SHIP-1",
                "Trade",
                "X1-AB-010",
                "X1-AB-099",
                "IRON_ORE",
                null,
                0,
                DateTimeOffset.UtcNow,
                null,
                0));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100, CargoCurrent: 0, CargoCapacity: 20));

        var services = new ServiceCollection();
        services.AddSingleton(assignments);
        services.AddSingleton(ships);
        services.AddSingleton(inOrbit);
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedTraderEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipUndockedEvent("SHIP-1", "X1-AB", "X1-AB-001", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipUndockedTraderEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipMovingEvent));

        await inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-010", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_UsesTraderHandler_DocksAtSellWaypoint_WhenShipHasCargoAndIsAtDestination()
    {
        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var inOrbit = Substitute.For<IInOrbitCommandAcceptor>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new SpaceTraders.Application.DTOs.ShipAssignmentDto(
                "SHIP-1",
                "Trade",
                "X1-AB-010",
                "X1-AB-099",
                "IRON_ORE",
                null,
                0,
                DateTimeOffset.UtcNow,
                null,
                0));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-099", "IN_ORBIT", "CRUISE", 100, 100, CargoCurrent: 10, CargoCapacity: 20));

        var services = new ServiceCollection();
        services.AddSingleton(assignments);
        services.AddSingleton(ships);
        services.AddSingleton(inOrbit);
        services.AddSingleton<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipUndockedTraderEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipUndockedEvent("SHIP-1", "X1-AB", "X1-AB-099", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipUndockedTraderEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipDockedEvent));

        await inOrbit.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());
    }
}
