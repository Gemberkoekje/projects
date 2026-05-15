using SpaceTraders.Application.Automation;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IScoutPlanRepository
{
    Task<ScoutAllMarketplacesPlanState?> GetAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(ScoutAllMarketplacesPlanState state, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
