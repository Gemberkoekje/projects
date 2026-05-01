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
        var purgedDeleted = await db.AgentCreditsSamples
            .Where(s => s.AgentToken == db.AgentToken && s.ObservedAt < aggregateRetentionCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return downsampledDeleted + purgedDeleted;
    }
}
