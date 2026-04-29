using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipDockedEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_Assigns_ForShipDockedEvent()
    {
        var bus = Substitute.For<IMessageBus>();
        var handler = new ShipDockedEventHandler(bus, NullLogger<ShipDockedEventHandler>.Instance);

        var @event = new ShipDockedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await handler.HandleAsync(@event, CancellationToken.None);

        result.Should().BeEquivalentTo(ChainOfCommandHandlerResult.Handled());
        await bus.Received(1).InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Assigns_ForShipIdleDockedEvent()
    {
        var bus = Substitute.For<IMessageBus>();
        var handler = new ShipDockedEventHandler(bus, NullLogger<ShipDockedEventHandler>.Instance);

        var @event = new ShipIdleDockedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            "already idle",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await handler.HandleAsync(@event, CancellationToken.None);

        result.Should().BeEquivalentTo(ChainOfCommandHandlerResult.Handled());
        await bus.Received(1).InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Assigns_ForShipAssignmentTypeSetEvent()
    {
        var bus = Substitute.For<IMessageBus>();
        var handler = new ShipDockedEventHandler(bus, NullLogger<ShipDockedEventHandler>.Instance);

        var @event = new ShipAssignmentTypeSetEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            "Trade",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await handler.HandleAsync(@event, CancellationToken.None);

        result.Should().BeEquivalentTo(ChainOfCommandHandlerResult.Handled());
        await bus.Received(1).InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}

