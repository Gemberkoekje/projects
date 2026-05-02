using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Wolverine handler that receives <see cref="ShipInTransitEvent"/> for logging/observability purposes.
/// Phase 7d: converted from IChainOfCommandEventHandler to a plain Wolverine handler.
/// Phase 14c: tick scheduling removed; the <c>ShipEventScheduler</c> now fires
/// <c>ShipArrivedEvent</c> at the correct arrival time instead of scheduling a polling tick.
/// </summary>
public sealed class ShipInTransitEventHandler
{
    public Task Handle(ShipInTransitEvent @event, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
