using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class TradeOpportunityRepository(SpaceTradersDbContext db) : ITradeOpportunityRepository
{
    public async Task<IReadOnlyList<TradeOpportunityDto>> GetTopRoutesAsync(int maxResults = 10, CancellationToken cancellationToken = default)
    {
        var entities = await db.TradeOpportunities
            .AsNoTracking()
            .OrderByDescending(t => t.ProfitPerJump)
            .Take(maxResults)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    public async Task<TradeOpportunityDto?> GetBestRouteForCapacityAsync(int cargoCapacity, int minProfitPerUnit, int maxDistanceJumps, CancellationToken cancellationToken = default)
    {
        var entity = await db.TradeOpportunities
            .AsNoTracking()
            .Where(t => t.ProfitPerUnit >= minProfitPerUnit && t.DistanceJumps <= maxDistanceJumps)
            .OrderByDescending(t => t.ProfitPerJump)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : MapToDto(entity);
    }

    public async Task ReplaceAllAsync(IReadOnlyList<TradeOpportunityDto> opportunities, CancellationToken cancellationToken = default)
    {
        var existing = await db.TradeOpportunities.ToListAsync(cancellationToken);
        db.TradeOpportunities.RemoveRange(existing);

        foreach (var dto in opportunities)
        {
            db.TradeOpportunities.Add(new TradeOpportunity
            {
                TradeSymbol = dto.TradeSymbol,
                BuyWaypoint = dto.BuyWaypoint,
                SellWaypoint = dto.SellWaypoint,
                BuyPrice = dto.BuyPrice,
                SellPrice = dto.SellPrice,
                ProfitPerUnit = dto.ProfitPerUnit,
                DistanceJumps = dto.DistanceJumps,
                ProfitPerJump = dto.ProfitPerJump,
                ComputedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static TradeOpportunityDto MapToDto(TradeOpportunity entity) =>
        new(entity.Id, entity.TradeSymbol, entity.BuyWaypoint, entity.SellWaypoint,
            entity.BuyPrice, entity.SellPrice, entity.ProfitPerUnit,
            entity.DistanceJumps, entity.ProfitPerJump, entity.ComputedAt);
}
