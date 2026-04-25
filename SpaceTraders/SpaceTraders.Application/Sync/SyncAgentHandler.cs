using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.Application.Sync;

public sealed class SyncAgentHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    ILogger<SyncAgentHandler> logger)
{
    public async Task Handle(SyncAgentCommand command, CancellationToken cancellationToken)
    {
        var agent = await apiClient.GetMyAgentAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var cached = await db.Agents.FindAsync([agent.Symbol], cancellationToken);
        if (cached is null)
        {
            cached = new CachedAgent
            {
                Symbol = agent.Symbol,
                AccountId = agent.AccountId,
                HeadquartersSymbol = agent.Headquarters,
                StartingFaction = agent.StartingFaction,
                Credits = agent.Credits,
                ShipCount = agent.ShipCount,
                LastSyncedAt = now
            };
            db.Agents.Add(cached);
        }
        else
        {
            cached.AccountId = agent.AccountId;
            cached.HeadquartersSymbol = agent.Headquarters;
            cached.StartingFaction = agent.StartingFaction;
            cached.Credits = agent.Credits;
            cached.ShipCount = agent.ShipCount;
            cached.LastSyncedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Agent {Symbol} synced. Credits: {Credits}", agent.Symbol, agent.Credits);
    }
}
