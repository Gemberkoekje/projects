using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.Events.Dispatching;

public interface IChainOfCommandDispatcher
{
    Task<ChainOfCommandDispatchResult> DispatchAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : ChainOfCommandEvent;
}
