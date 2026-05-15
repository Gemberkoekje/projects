using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ProbeDeploymentPlanRepository(IPlanRepository planRepository) : IProbeDeploymentPlanRepository
{
    public Task<ProbeDeploymentPlanState?> GetAsync(CancellationToken cancellationToken = default) =>
        planRepository.GetAsync<ProbeDeploymentPlanState>(PlanTypes.ProbeDeployment, cancellationToken);

    public Task UpsertAsync(ProbeDeploymentPlanState state, CancellationToken cancellationToken = default) =>
        planRepository.UpsertAsync(PlanTypes.ProbeDeployment, state, cancellationToken);

    public Task DeleteAsync(CancellationToken cancellationToken = default) =>
        planRepository.DeleteAsync(PlanTypes.ProbeDeployment, cancellationToken);
}
