using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class AgentCreditsSampleRepository(SpaceTradersDbContext db) : IAgentCreditsSampleRepository
{
    public async Task AppendAsync(long credits, CancellationToken cancellationToken = default)
    {
        var sample = new AgentCreditsSample
        {
            AgentToken = db.AgentToken,
            ObservedAt = TimeProvider.System.GetUtcNow(),
            Credits = credits,
        };

        db.AgentCreditsSamples.Add(sample);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CreditsSampleDto>> GetRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.AgentCreditsSamples
            .AsNoTracking()
            .Where(s => s.ObservedAt >= from && s.ObservedAt <= to)
            .OrderBy(s => s.ObservedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(s => new CreditsSampleDto(s.ObservedAt, s.Credits)).ToList();
    }

    public async Task<int> PruneAsync(DateTimeOffset rawRetentionCutoff, DateTimeOffset aggregateRetentionCutoff, CancellationToken cancellationToken = default)
    {
        // Step 1: Downsample the 7–90 day window — keep one row per hour (lowest id), delete duplicates.
        var downsampledDeleted = await db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM agent_credits_samples
            WHERE agent_token = {db.AgentToken}
              AND observed_at < {rawRetentionCutoff}
              AND observed_at >= {aggregateRetentionCutoff}
              AND id NOT IN (
                SELECT MIN(id)
                FROM agent_credits_samples
                WHERE agent_token = {db.AgentToken}
                  AND observed_at < {rawRetentionCutoff}
                  AND observed_at >= {aggregateRetentionCutoff}
                GROUP BY date_trunc('hour', observed_at)
              )
            """, cancellationToken);

        // Step 2: Delete all rows older than the 90-day aggregate retention cutoff.
        // Note: AgentCreditsSample has no global query filter (unlike MarketPriceSample),
        // so agent_token must be filtered explicitly here.
        var purgedDeleted = await db.AgentCreditsSamples
            .Where(s => s.AgentToken == db.AgentToken && s.ObservedAt < aggregateRetentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return downsampledDeleted + purgedDeleted;
    }
}
