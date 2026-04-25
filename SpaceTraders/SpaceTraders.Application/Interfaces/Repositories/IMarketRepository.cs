using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IMarketRepository
{
    Task<DateTimeOffset?> GetLastObservedAtAsync(string waypointSymbol, CancellationToken cancellationToken = default);
    Task UpsertAsync(MarketDataModel market, CancellationToken cancellationToken = default);
}
