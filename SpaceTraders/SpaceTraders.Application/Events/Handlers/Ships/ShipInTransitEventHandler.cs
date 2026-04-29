using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles ShipInTransitEvent by skipping it (doing nothing).
/// The NavigateShipCommand is responsible for emitting the delayed ShipArrivedEvent
/// when the ship's arrival time is reached.
/// </summary>
public sealed class ShipInTransitEventHandler : IChainOfCommandEventHandler<ShipInTransitEvent>
{
    public int Priority => 90;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipInTransitEvent @event, CancellationToken cancellationToken)
    {
        return Task.FromResult(ChainOfCommandHandlerResult.Handled());
    }
}
