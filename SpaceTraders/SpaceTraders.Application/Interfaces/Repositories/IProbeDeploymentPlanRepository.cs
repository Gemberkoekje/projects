using SpaceTraders.Application.Automation;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IProbeDeploymentPlanRepository
{
    Task<ProbeDeploymentPlanState?> GetAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(ProbeDeploymentPlanState state, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
