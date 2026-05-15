using SpaceTraders.Application.Automation;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IContractMineralPlanRepository
{
    Task<ContractMineralPlanState?> GetAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(ContractMineralPlanState state, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
