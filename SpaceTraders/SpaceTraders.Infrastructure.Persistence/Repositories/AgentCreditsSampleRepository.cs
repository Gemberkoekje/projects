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
            AgentId = db.AgentId,
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
}
