using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class SystemRepository(SpaceTradersDbContext db) : ISystemRepository
{
    public async Task<SystemCacheModel?> FindAsync(string systemSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Systems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Symbol == systemSymbol, cancellationToken);

        return entity is null
            ? null
            : new SystemCacheModel(entity.Symbol, entity.SectorSymbol, entity.Type, entity.X, entity.Y, entity.LastObservedAt);
    }

    public async Task UpsertAsync(SystemCacheModel system, CancellationToken cancellationToken = default)
    {
        var existing = await db.Systems.FindAsync([db.AgentToken, system.Symbol], cancellationToken);
        var values = new CachedSystem
        {
            AgentToken = db.AgentToken,
            Symbol = system.Symbol,
            SectorSymbol = system.SectorSymbol,
            Type = system.Type,
            X = system.X,
            Y = system.Y,
            LastObservedAt = system.LastObservedAt,
        };

        if (existing is null)
        {
            db.Systems.Add(values);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(values);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
