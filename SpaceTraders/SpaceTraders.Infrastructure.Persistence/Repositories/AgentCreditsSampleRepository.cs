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
}
