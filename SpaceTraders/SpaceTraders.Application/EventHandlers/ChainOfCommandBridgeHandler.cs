using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Wolverine message handler that routes incoming chain-of-command events to the
/// <see cref="IChainOfCommandDispatcher"/>.
/// Phase 7b: Overloads for ShipDockedEvent, ShipIdleDockedEvent, ShipRefueledEvent,
/// ShipAssignmentTypeSetEvent, and ShipRoleSetEvent removed — docked routing deleted.
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

    public async Task Handle(ShipArrivedEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipArrivedEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync<ShipInOrbitEvent>(@event, cancellationToken);
    }
}
