using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipInOrbitEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_EmitsShipUndockedEvent()
    {
        var bus = Substitute.For<IMessageBus>();

        var services = new ServiceCollection();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipInOrbitEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-001",
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipInOrbitEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipUndockedEvent));
        result.IsScheduled.Should().BeFalse();

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipUndockedEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.SystemSymbol == "X1-AB" &&
                e.WaypointSymbol == "X1-AB-001"),
            Arg.Any<DeliveryOptions>());
    }
}
