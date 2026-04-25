using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IShipyardRepository
{
    Task<DateTimeOffset?> GetLastObservedAtAsync(string waypointSymbol, CancellationToken cancellationToken = default);
    Task<string?> FindShipyardForTypeAsync(string shipType, CancellationToken cancellationToken = default);
    Task UpsertAsync(ShipyardDataModel shipyard, CancellationToken cancellationToken = default);
}
