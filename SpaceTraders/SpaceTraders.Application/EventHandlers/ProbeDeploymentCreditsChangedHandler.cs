using SpaceTraders.Application.Automation;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Wakes deferred probe deployment evaluation when agent credits change.
/// </summary>
public sealed class ProbeDeploymentCreditsChangedHandler(IProbeDeploymentPlanService probeDeployment)
{
    public Task Handle(AgentCreditsChangedEvent @event, CancellationToken cancellationToken)
    {
        return probeDeployment.OnCreditsChangedAsync(cancellationToken);
    }
}
