using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ContractRepository(SpaceTradersDbContext db) : IContractRepository
{
    public async Task<ContractDto?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Contracts.FindAsync([id], cancellationToken);
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
        var existing = await db.Contracts.FindAsync([contract.Id], cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            db.Contracts.Add(new CachedContract
            {
                Id = contract.Id,
                FactionSymbol = contract.FactionSymbol,
                Type = contract.Type,
                IsAccepted = contract.IsAccepted,
                IsFulfilled = contract.IsFulfilled,
                Expiration = contract.Expiration,
                DeadlineToAccept = contract.DeadlineToAccept,
                LastSyncedAt = now
            });
        }
        else
        {
            existing.FactionSymbol = contract.FactionSymbol;
            existing.Type = contract.Type;
            existing.IsAccepted = contract.IsAccepted;
            existing.IsFulfilled = contract.IsFulfilled;
            existing.Expiration = contract.Expiration;
            existing.DeadlineToAccept = contract.DeadlineToAccept;
            existing.LastSyncedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(string id, bool isAccepted, bool isFulfilled, CancellationToken cancellationToken = default)
    {
        var entity = await db.Contracts.FindAsync([id], cancellationToken);
        if (entity is null) return;

        entity.IsAccepted = isAccepted;
        entity.IsFulfilled = isFulfilled;
        entity.LastSyncedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ContractDto MapToDto(CachedContract entity) =>
        new(entity.Id, entity.FactionSymbol, entity.Type, entity.IsAccepted, entity.IsFulfilled, entity.Expiration, entity.DeadlineToAccept);
}
