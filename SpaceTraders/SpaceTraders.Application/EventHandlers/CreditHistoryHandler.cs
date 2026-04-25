using SpaceTraders.Application.Interfaces;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Records the agent's credit balance into the in-memory <see cref="ICreditHistoryService"/>
/// whenever credits change.
/// </summary>
public sealed class CreditHistoryHandler(ICreditHistoryService creditHistory)
{
    public void Handle(AgentCreditsChangedEvent @event)
    {
        creditHistory.Record(@event.NewCredits);
    }
}
