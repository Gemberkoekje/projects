using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Wolverine message handler that routes incoming chain-of-command events to the
/// <see cref="IChainOfCommandDispatcher"/>.
/// Phase 7c: Overloads for ShipUndockedEvent, ShipArrivedEvent, and ShipInOrbitEvent removed —
/// orbit routing deleted. Only ShipInTransitEvent remains (Phase 7d will remove it).
/// </summary>
public sealed class ChainOfCommandBridgeHandler(
    IChainOfCommandDispatcher dispatcher,
    ILogger<ChainOfCommandBridgeHandler> logger)
{
    public async Task Handle(ShipInTransitEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug("Chain: routing {Event} for ship {Ship}.", nameof(ShipInTransitEvent), @event.ShipSymbol);
        await dispatcher.DispatchAsync(@event, cancellationToken);
    }
}
