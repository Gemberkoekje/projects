using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Wolverine message handler that routes incoming chain-of-command events to the
/// <see cref="IChainOfCommandDispatcher"/> and terminates the chain when a
/// <see cref="ShipIdleEvent"/> is received by publishing a <see cref="ShipBecameIdleEvent"/>.
/// </summary>
public sealed class ChainOfCommandBridgeHandler(
    IChainOfCommandDispatcher dispatcher,
    IMessageBus bus,
    ILogger<ChainOfCommandBridgeHandler> logger)
{
    public async Task Handle(ShipUndockedEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipUndockedEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public async Task Handle(ShipMovingEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipMovingEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public async Task Handle(ShipInTransitEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipInTransitEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public async Task Handle(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipInOrbitEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public async Task Handle(ShipStateMismatchEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipStateMismatchEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public async Task Handle(ShipArrivedEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipArrivedEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public async Task Handle(ShipRoleSetEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipRoleSetEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public async Task Handle(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipDockedEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public async Task Handle(ShipRefueledEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipRefueledEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public async Task Handle(ShipIdleEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: ship {Ship} became idle. Reason: {Reason}.", @event.ShipSymbol, @event.Reason);
        await bus.PublishAsync(new ShipBecameIdleEvent(@event.ShipSymbol, @event.Reason));
    }
}
