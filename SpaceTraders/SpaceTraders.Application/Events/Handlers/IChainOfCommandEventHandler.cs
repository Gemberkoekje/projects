using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.Events.Handlers;

public interface IChainOfCommandEventHandler<in TEvent>
    where TEvent : ChainOfCommandEvent
{
    int Priority { get; }

    Task<ChainOfCommandHandlerResult> HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
