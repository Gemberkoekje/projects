using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Dispatching;

public sealed class ChainOfCommandDispatcher(
    IServiceProvider serviceProvider,
    IMessageBus bus,
    ILogger<ChainOfCommandDispatcher> logger) : IChainOfCommandDispatcher
{
    public async Task<ChainOfCommandDispatchResult> DispatchAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : ChainOfCommandEvent
    {
        var handlers = serviceProvider.GetServices<IChainOfCommandEventHandler<TEvent>>()
            .OrderBy(h => h.Priority)
            .ToList();

        if (handlers.Count == 0)
        {
            throw new InvalidOperationException($"No chain-of-command handlers registered for event type {typeof(TEvent).Name}.");
        }

        foreach (var handler in handlers)
        {
            var result = await handler.HandleAsync(@event, cancellationToken);

            if (result is SkippedChainOfCommandHandlerResult)
            {
                continue;
            }

            if (@event is ShipAssignmentTypeSetEvent shipAssignmentTypeSetEvent)
            {
                logger.LogInformation(
                    "Chain decision: event={EventType}, ship={ShipSymbol}, assignmentType={AssignmentType}, handler={Handler}, result={Result}",
                    typeof(TEvent).Name,
                    shipAssignmentTypeSetEvent.ShipSymbol,
                    shipAssignmentTypeSetEvent.AssignmentType,
                    handler.GetType().Name,
                    result.GetType().Name);
            }
            else
            {
                logger.LogInformation(
                    "Chain decision: event={EventType}, handler={Handler}, result={Result}",
                    typeof(TEvent).Name,
                    handler.GetType().Name,
                    result.GetType().Name);
            }

            if (result is HandledWithoutNextEventChainOfCommandHandlerResult)
            {
                return new ChainOfCommandDispatchResult(
                    handler.GetType().Name,
                    "Handled",
                    string.Empty,
                    false);
            }

            if (result is HandledChainOfCommandHandlerResult handled)
            {
                if (handled.IsScheduled)
                {
                    await bus.ScheduleAsync(handled.NextEvent, handled.ScheduledFor);
                }
                else
                {
                    await bus.PublishAsync(handled.NextEvent);
                }

                return new ChainOfCommandDispatchResult(
                    handler.GetType().Name,
                    "Handled",
                    handled.NextEvent.GetType().Name,
                    handled.IsScheduled);
            }

            return new ChainOfCommandDispatchResult(
                handler.GetType().Name,
                "Failed",
                string.Empty,
                false);
        }

        return new ChainOfCommandDispatchResult(
            typeof(TEvent).Name,
            "Failed",
            string.Empty,
            false);
    }
}
