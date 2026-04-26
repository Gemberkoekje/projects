using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles <see cref="ShipDockedEvent"/> by emitting a <see cref="ShipIdleDockedEvent"/>.
/// Docked fallback transitions into the idle-docked decision chain.
/// </summary>
public sealed class ShipDockedEventHandler : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 1000;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var idleDockedEvent = new ShipIdleDockedEvent(
            @event.ShipSymbol,
            @event.SystemSymbol,
            @event.WaypointSymbol,
            "Ship docked; entering idle docked role selection.",
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return Task.FromResult(ChainOfCommandHandlerResult.Handled(idleDockedEvent));
    }
}
