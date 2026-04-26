using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class AgentRepository(SpaceTradersDbContext db) : IAgentRepository
{
    public async Task<AgentModel?> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await db.Agents.AsNoTracking().OrderBy(a => a.Symbol).FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : MapToModel(entity);
    }

    public async Task UpsertAsync(AgentModel agent, CancellationToken cancellationToken = default)
    {
        var existing = await db.Agents.FindAsync([db.AgentToken, agent.Symbol], cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        var values = new CachedAgent
        {
            AgentToken = db.AgentToken,
            Symbol = agent.Symbol,
            AccountId = agent.AccountId,
            HeadquartersSymbol = agent.HeadquartersSymbol,
            StartingFaction = agent.StartingFaction,
            Credits = agent.Credits,
            ShipCount = agent.ShipCount,
            LastSyncedAt = now
        };

        if (existing is null)
        {
            db.Agents.Add(values);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(values);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static AgentModel MapToModel(CachedAgent entity) =>
        new(entity.Symbol, entity.AccountId, entity.HeadquartersSymbol, entity.Credits, entity.StartingFaction, entity.ShipCount);
}
