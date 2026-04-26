using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IShipRepository
{
    Task<ShipModel?> FindAsync(string symbol, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShipModel>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShipModel>> GetInTransitAsync(CancellationToken cancellationToken = default);

    Task<bool> IsShipAtWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task UpsertAsync(ShipModel ship, CancellationToken cancellationToken = default);

    Task UpdateNavAsync(string symbol, NavModel nav, FuelModel? fuel, CancellationToken cancellationToken = default);

    Task UpdateCargoAsync(string symbol, CargoModel cargo, CancellationToken cancellationToken = default);

    Task UpdateFuelAsync(string symbol, FuelModel fuel, CancellationToken cancellationToken = default);
}
