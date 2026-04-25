using SpaceTraders.Application.DTOs;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IContractRepository
{
    Task<ContractDto?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContractDto>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(ContractDto contract, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(string id, bool isAccepted, bool isFulfilled, CancellationToken cancellationToken = default);
}
