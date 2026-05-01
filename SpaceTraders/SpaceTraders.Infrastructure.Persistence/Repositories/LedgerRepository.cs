using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class LedgerRepository(SpaceTradersDbContext db, IActiveRunIdProvider activeRunIdProvider) : ILedgerRepository
{
    public async Task AppendAsync(
        string shipSymbol,
        LedgerCategory category,
        long amount,
        string? goodSymbol = null,
        int? unitPrice = null,
        int? units = null,
        string? waypointSymbol = null,
        string? sourceEventId = null,
        Guid? runId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new LedgerEntry
        {
            AgentToken = db.AgentToken,
            OccurredAt = TimeProvider.System.GetUtcNow(),
            ShipSymbol = shipSymbol,
            RunId = runId ?? activeRunIdProvider.ActiveRunId,
            Category = category,
            Amount = amount,
            GoodSymbol = goodSymbol,
            UnitPrice = unitPrice,
            Units = units,
            WaypointSymbol = waypointSymbol,
            SourceEventId = sourceEventId,
        };

        db.LedgerEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        return await db.LedgerEntries
            .Where(e => e.OccurredAt < olderThan)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
