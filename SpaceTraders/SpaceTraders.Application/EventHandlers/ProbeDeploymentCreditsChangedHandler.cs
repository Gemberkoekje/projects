using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Wakes deferred probe deployment evaluation when agent credits change, unless automation or the
/// probe plan is switched off: that can buy probes, and a plan that is off buys nothing (D9).
/// </summary>
public sealed class ProbeDeploymentCreditsChangedHandler(
    IProbeDeploymentPlanService probeDeployment,
    ISettingsRepository settings)
{
    public async Task Handle(AgentCreditsChangedEvent @event, CancellationToken cancellationToken)
    {
        if (!await settings.IsAutomationEnabledAsync(cancellationToken)
            || !await settings.IsPlanEnabledAsync(AutomationPlan.ProbeDeployment, cancellationToken))
        {
            return;
        }

        await probeDeployment.OnCreditsChangedAsync(cancellationToken);
    }
}
