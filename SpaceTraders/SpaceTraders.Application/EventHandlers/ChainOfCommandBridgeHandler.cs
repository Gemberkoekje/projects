using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Wolverine message handler that routes incoming chain-of-command events to the
/// <see cref="IChainOfCommandDispatcher"/>.
/// </summary>
public sealed class ChainOfCommandBridgeHandler(
    IChainOfCommandDispatcher dispatcher,
    ILogger<ChainOfCommandBridgeHandler> logger)
{
    public async Task Handle(ShipUndockedEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipUndockedEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync<ShipInOrbitEvent>(@event, cancellationToken);
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
        await dispatcher.DispatchAsync<ShipInOrbitEvent>(@event, cancellationToken);
    }

    public async Task Handle(ShipRoleSetEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipRoleSetEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync<ShipDockedEvent>(@event, cancellationToken);
    }

    public async Task Handle(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipDockedEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public async Task Handle(ShipIdleDockedEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipIdleDockedEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync<ShipDockedEvent>(@event, cancellationToken);
    }

    public async Task Handle(ShipRefueledEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipRefueledEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync<ShipDockedEvent>(@event, cancellationToken);
    }

    public async Task Handle(ShipAssignmentTypeSetEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipAssignmentTypeSetEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync<ShipDockedEvent>(@event, cancellationToken);
    }
}
