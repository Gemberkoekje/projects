using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class MarketRepository(SpaceTradersDbContext db) : IMarketRepository
{
    public async Task<DateTimeOffset?> GetLastObservedAtAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Markets.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WaypointSymbol == waypointSymbol, cancellationToken);
        return entity?.LastObservedAt;
    }

    public async Task UpsertAsync(MarketDataModel market, CancellationToken cancellationToken = default)
    {
        var existing = await db.Markets.FindAsync([market.WaypointSymbol], cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            db.Markets.Add(new CachedMarket
            {
                WaypointSymbol = market.WaypointSymbol,
                SystemSymbol = market.SystemSymbol,
                TradeGoodsJson = market.TradeGoodsJson,
                ImportsJson = market.ImportsJson,
                ExportsJson = market.ExportsJson,
                ExchangeJson = market.ExchangeJson,
                LastObservedAt = now
            });
        }
        else
        {
            existing.SystemSymbol = market.SystemSymbol;
            existing.TradeGoodsJson = market.TradeGoodsJson;
            existing.ImportsJson = market.ImportsJson;
            existing.ExportsJson = market.ExportsJson;
            existing.ExchangeJson = market.ExchangeJson;
            existing.LastObservedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
