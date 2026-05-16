using SpaceTraders.Application.DTOs;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IShipAssignmentRepository
{
    Task<ShipAssignmentDto?> FindAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShipAssignmentDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(ShipAssignmentDto assignment, CancellationToken cancellationToken = default);
}
