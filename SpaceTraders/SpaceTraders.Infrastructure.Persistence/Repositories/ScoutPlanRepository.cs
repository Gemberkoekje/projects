using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ScoutPlanRepository(IPlanRepository planRepository) : IScoutPlanRepository
{
    public Task<ScoutAllMarketplacesPlanState?> GetAsync(CancellationToken cancellationToken = default) =>
        planRepository.GetAsync<ScoutAllMarketplacesPlanState>(PlanTypes.ScoutAllMarketplaces, cancellationToken);

    public Task UpsertAsync(ScoutAllMarketplacesPlanState state, CancellationToken cancellationToken = default) =>
        planRepository.UpsertAsync(PlanTypes.ScoutAllMarketplaces, state, cancellationToken);

    public Task DeleteAsync(CancellationToken cancellationToken = default) =>
        planRepository.DeleteAsync(PlanTypes.ScoutAllMarketplaces, cancellationToken);
}
