using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Persists agent credit balance snapshots to the <see cref="IAgentCreditsSampleRepository"/>
/// on every <see cref="AgentCreditsChangedEvent"/> and notifies dashboard clients.
/// </summary>
public sealed class AgentCreditsSampleHandler(
    IAgentCreditsSampleRepository creditsSamples,
    IDashboardNotifier notifier)
{
    public async Task Handle(AgentCreditsChangedEvent @event, CancellationToken cancellationToken)
    {
        await creditsSamples.AppendAsync(@event.NewCredits, cancellationToken);
        notifier.Notify("agent-credits", "agent");
    }
}
