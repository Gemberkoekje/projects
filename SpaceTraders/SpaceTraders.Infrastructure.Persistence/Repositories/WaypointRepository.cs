using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class WaypointRepository(SpaceTradersDbContext db) : IWaypointRepository
{
    public async Task<IReadOnlyList<WaypointCacheModel>> GetBySystemAsync(
        string systemSymbol,
        CancellationToken cancellationToken = default)
    {
        var entities = await db.Waypoints
            .AsNoTracking()
            .Where(w => w.SystemSymbol == systemSymbol)
            .OrderBy(w => w.Symbol)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task<IReadOnlyList<WaypointCacheModel>> GetUnscoutedOrStaleAsync(
        string systemSymbol,
        TimeSpan staleness,
        CancellationToken cancellationToken = default)
    {
        var threshold = DateTimeOffset.UtcNow - staleness;
        var entities = await db.Waypoints
            .AsNoTracking()
            .Where(w => w.SystemSymbol == systemSymbol && w.LastObservedAt < threshold)
            .OrderBy(w => w.LastObservedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task MarkVisitedAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Waypoints.FindAsync([waypointSymbol], cancellationToken);
        if (entity is null) return;

        entity.LastObservedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static WaypointCacheModel MapToModel(CachedWaypoint entity) =>
        new(entity.Symbol, entity.SystemSymbol, entity.Type, entity.X, entity.Y,
            entity.HasMarket, entity.HasShipyard, entity.LastObservedAt);
}
