using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class WaypointRepository(SpaceTradersDbContext db) : IWaypointRepository
{
    public async Task<WaypointCacheModel?> FindAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Waypoints
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Symbol == waypointSymbol, cancellationToken);

        return entity is null ? null : MapToModel(entity);
    }

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
        var threshold = TimeProvider.System.GetUtcNow() - staleness;
        var entities = await db.Waypoints
            .AsNoTracking()
            .Where(w => w.SystemSymbol == systemSymbol && w.LastObservedAt < threshold)
            .OrderBy(w => w.LastObservedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task UpsertRangeAsync(IReadOnlyList<WaypointCacheModel> waypoints, CancellationToken cancellationToken = default)
    {
        if (waypoints.Count == 0)
        {
            return;
        }

        var symbols = waypoints.Select(w => w.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = await db.Waypoints
            .Where(w => symbols.Contains(w.Symbol))
            .ToDictionaryAsync(w => w.Symbol, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var waypoint in waypoints)
        {
            var values = new CachedWaypoint
            {
                AgentToken = db.AgentToken,
                Symbol = waypoint.Symbol,
                SystemSymbol = waypoint.SystemSymbol,
                Type = waypoint.Type,
                X = waypoint.X,
                Y = waypoint.Y,
                HasMarket = waypoint.HasMarket,
                HasShipyard = waypoint.HasShipyard,
                TraitsJson = waypoint.TraitsJson,
                ModifiersJson = waypoint.ModifiersJson,
                OrbitalsJson = waypoint.OrbitalsJson,
                ParentSymbol = waypoint.ParentSymbol,
                IsUnderConstruction = waypoint.IsUnderConstruction,
                ChartJson = waypoint.ChartJson,
                LastObservedAt = waypoint.LastObservedAt,
            };

            if (existing.TryGetValue(waypoint.Symbol, out var entity))
            {
                db.Entry(entity).CurrentValues.SetValues(values);
            }
            else
            {
                db.Waypoints.Add(values);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkVisitedAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        await db.Waypoints
            .Where(w => w.AgentToken == db.AgentToken && w.Symbol == waypointSymbol)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(w => w.LastObservedAt, TimeProvider.System.GetUtcNow()),
                cancellationToken);
    }

    private static WaypointCacheModel MapToModel(CachedWaypoint entity) =>
        new(entity.Symbol, entity.SystemSymbol, entity.Type, entity.X, entity.Y,
            entity.HasMarket, entity.HasShipyard, entity.LastObservedAt,
            entity.TraitsJson, entity.ModifiersJson, entity.OrbitalsJson,
            entity.ParentSymbol, entity.IsUnderConstruction, entity.ChartJson);
}
