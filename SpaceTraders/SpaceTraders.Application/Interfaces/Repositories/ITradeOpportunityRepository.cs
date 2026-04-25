using SpaceTraders.Application.DTOs;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface ITradeOpportunityRepository
{
    Task<IReadOnlyList<TradeOpportunityDto>> GetTopRoutesAsync(int maxResults = 10, CancellationToken cancellationToken = default);
    Task<TradeOpportunityDto?> GetBestRouteForCapacityAsync(int cargoCapacity, int minProfitPerUnit, int maxDistanceJumps, CancellationToken cancellationToken = default);
    Task ReplaceAllAsync(IReadOnlyList<TradeOpportunityDto> opportunities, CancellationToken cancellationToken = default);
}
