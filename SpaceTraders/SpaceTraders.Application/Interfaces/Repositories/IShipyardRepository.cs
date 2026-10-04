using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IShipyardRepository
{
    Task<DateTimeOffset?> GetLastObservedAtAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    /// <summary>The shipyard seen most recently that sells <paramref name="shipType"/>, among those in <paramref name="systems"/>.</summary>
    /// <param name="shipType">The ship type.</param>
    /// <param name="systems">The systems to look in: those the plans do business in (<c>BusinessSystems</c>).</param>
    /// <param name="cancellationToken">Stops the query.</param>
    /// <returns>The shipyard's waypoint, or null when none there sells it.</returns>
    Task<string?> FindShipyardForTypeAsync(string shipType, IReadOnlyCollection<string> systems, CancellationToken cancellationToken = default);

    Task UpsertAsync(ShipyardDataModel shipyard, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShipyardWaypointDto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ShipyardWaypointDto?> FindByWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShipyardFreshnessDto>> GetAllFreshnessAsync(CancellationToken cancellationToken = default);
}
