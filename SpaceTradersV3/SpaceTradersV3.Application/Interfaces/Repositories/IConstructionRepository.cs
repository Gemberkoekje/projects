using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IConstructionRepository
{
    Task<ConstructionSiteModel?> FindAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConstructionSiteModel>> GetIncompleteAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(ConstructionSiteModel site, string systemSymbol, CancellationToken cancellationToken = default);
}
