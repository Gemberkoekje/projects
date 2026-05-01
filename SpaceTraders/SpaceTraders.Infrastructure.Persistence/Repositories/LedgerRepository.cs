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

    public async Task<IReadOnlyList<LedgerEntryDto>> GetRangeAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? shipSymbol = null,
        LedgerCategory? category = null,
        Guid? runId = null,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var query = db.LedgerEntries.AsNoTracking();

        if (from.HasValue)
            query = query.Where(e => e.OccurredAt >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.OccurredAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(shipSymbol))
            query = query.Where(e => e.ShipSymbol == shipSymbol);
        if (category.HasValue)
            query = query.Where(e => e.Category == category.Value);
        if (runId.HasValue)
            query = query.Where(e => e.RunId == runId.Value);

        var rows = await query
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(e => new LedgerEntryDto(
                e.Id, e.OccurredAt, e.ShipSymbol, e.RunId,
                e.Category.ToString(), e.Amount, e.GoodSymbol,
                e.UnitPrice, e.Units, e.WaypointSymbol))
            .ToList();
    }

    public async Task<IReadOnlyList<LedgerSummaryDto>> GetSummaryAsync(Guid? runId = null, CancellationToken cancellationToken = default)
    {
        var query = db.LedgerEntries.AsNoTracking();

        if (runId.HasValue)
            query = query.Where(e => e.RunId == runId.Value);

        var rows = await query
            .GroupBy(e => e.Category)
            .Select(g => new { Category = g.Key.ToString(), TotalAmount = g.Sum(e => e.Amount), EntryCount = g.Count() })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new LedgerSummaryDto(r.Category, r.TotalAmount, r.EntryCount))
            .ToList();
    }

    public async Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        return await db.LedgerEntries
            .Where(e => e.OccurredAt < olderThan)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
