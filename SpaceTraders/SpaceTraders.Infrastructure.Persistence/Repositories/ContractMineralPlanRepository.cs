using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ContractMineralPlanRepository(IPlanRepository planRepository) : IContractMineralPlanRepository
{
    public Task<ContractMineralPlanState?> GetAsync(CancellationToken cancellationToken = default) =>
        planRepository.GetAsync<ContractMineralPlanState>(PlanTypes.ContractMineral, cancellationToken);

    public Task UpsertAsync(ContractMineralPlanState state, CancellationToken cancellationToken = default) =>
        planRepository.UpsertAsync(PlanTypes.ContractMineral, state, cancellationToken);

    public Task DeleteAsync(CancellationToken cancellationToken = default) =>
        planRepository.DeleteAsync(PlanTypes.ContractMineral, cancellationToken);
}
