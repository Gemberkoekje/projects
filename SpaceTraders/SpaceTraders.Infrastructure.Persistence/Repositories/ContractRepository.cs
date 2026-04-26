using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ContractRepository(SpaceTradersDbContext db) : IContractRepository
{
    public async Task<ContractDto?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Contracts.FindAsync([db.AgentToken, id], cancellationToken);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ContractDto>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var entities = await db.Contracts
            .AsNoTracking()
            .Where(c => !c.IsFulfilled)
            .OrderBy(c => c.Expiration)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    public async Task UpsertAsync(ContractDto contract, CancellationToken cancellationToken = default)
    {
        var existing = await db.Contracts.FindAsync([db.AgentToken, contract.Id], cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        var values = new CachedContract
        {
            AgentToken = db.AgentToken,
            Id = contract.Id,
            FactionSymbol = contract.FactionSymbol,
            Type = contract.Type,
            IsAccepted = contract.IsAccepted,
            IsFulfilled = contract.IsFulfilled,
            Expiration = contract.Expiration,
            DeadlineToAccept = contract.DeadlineToAccept,
            LastSyncedAt = now
        };

        if (existing is null)
        {
            db.Contracts.Add(values);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(values);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(string id, bool isAccepted, bool isFulfilled, CancellationToken cancellationToken = default)
    {
        await db.Contracts
            .Where(c => c.AgentToken == db.AgentToken && c.Id == id)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(c => c.IsAccepted, isAccepted)
                    .SetProperty(c => c.IsFulfilled, isFulfilled)
                    .SetProperty(c => c.LastSyncedAt, TimeProvider.System.GetUtcNow()),
                cancellationToken);
    }

    private static ContractDto MapToDto(CachedContract entity) =>
        new(entity.Id, entity.FactionSymbol, entity.Type, entity.IsAccepted, entity.IsFulfilled, entity.Expiration, entity.DeadlineToAccept);
}
