using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipUndockedScoutEventHandler : IChainOfCommandEventHandler<ShipUndockedEvent>
{
    public int Priority => 100;

    public Task<ChainOfCommandHandlerResult> HandleAsync(ShipUndockedEvent @event, CancellationToken cancellationToken)
    {
        return Task.FromResult(ChainOfCommandHandlerResult.Skipped());
    }
}
