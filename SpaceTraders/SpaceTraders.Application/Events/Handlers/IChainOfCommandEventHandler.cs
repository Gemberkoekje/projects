using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.Events.Handlers;

public interface IChainOfCommandEventHandler<in TEvent>
    where TEvent : ChainOfCommandEvent
{
    Task<ChainOfCommandHandlerResult> HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
