using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.Application.Sync;

public sealed class SyncContractsHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    ILogger<SyncContractsHandler> logger)
{
    public async Task Handle(SyncContractsCommand command, CancellationToken cancellationToken)
    {
        var page = 1;
        const int limit = 20;
        var now = DateTimeOffset.UtcNow;
        var total = 0;

        while (true)
        {
            var response = await apiClient.GetMyContractsAsync(page, limit, cancellationToken);

            foreach (var contract in response.Data)
            {
                var cached = await db.Contracts.FindAsync([contract.Id], cancellationToken);
                if (cached is null)
                {
                    db.Contracts.Add(new CachedContract
                    {
                        Id = contract.Id,
                        FactionSymbol = contract.FactionSymbol,
                        Type = contract.Type,
                        IsAccepted = contract.Accepted,
                        IsFulfilled = contract.Fulfilled,
                        Expiration = contract.Expiration,
                        DeadlineToAccept = contract.DeadlineToAccept,
                        LastSyncedAt = now
                    });
                }
                else
                {
                    cached.FactionSymbol = contract.FactionSymbol;
                    cached.Type = contract.Type;
                    cached.IsAccepted = contract.Accepted;
                    cached.IsFulfilled = contract.Fulfilled;
                    cached.Expiration = contract.Expiration;
                    cached.DeadlineToAccept = contract.DeadlineToAccept;
                    cached.LastSyncedAt = now;
                }
            }

            total += response.Data.Count;

            if (response.Data.Count == 0 || total >= response.Meta.Total)
                break;

            page++;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Synced {Count} contracts.", total);
    }
}
