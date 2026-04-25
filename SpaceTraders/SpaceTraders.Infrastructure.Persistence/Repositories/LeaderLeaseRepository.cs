using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implements the leader lease using a single row in the <c>leader_leases</c> PostgreSQL table.
/// Uses optimistic concurrency: if two instances race, only one will persist its row.
/// </summary>
public sealed class LeaderLeaseRepository(SpaceTradersDbContext db) : ILeaderLeaseRepository
{
    public async Task<bool> TryAcquireOrRenewAsync(
        string leaseKey,
        string holderId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var lease = await db.LeaderLeases
            .FirstOrDefaultAsync(l => l.Key == leaseKey, cancellationToken);

        if (lease is null)
        {
            db.LeaderLeases.Add(new LeaderLease
            {
                Key = leaseKey,
                HolderId = holderId,
                ExpiresAt = now + leaseDuration
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException)
            {
                // Another pod inserted the row concurrently.
                db.ChangeTracker.Clear();
                return false;
            }
        }

        if (lease.HolderId == holderId)
        {
            // We already hold the lease – renew it.
            lease.ExpiresAt = now + leaseDuration;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (lease.ExpiresAt <= now)
        {
            // Lease has expired – take it over using the known expiry time as
            // an optimistic concurrency guard to prevent two pods simultaneously
            // taking an expired lease.
            var expectedExpiry = lease.ExpiresAt;

            try
            {
                var updated = await db.LeaderLeases
                    .Where(l => l.Key == leaseKey && l.ExpiresAt == expectedExpiry)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(l => l.HolderId, holderId)
                            .SetProperty(l => l.ExpiresAt, now + leaseDuration),
                        cancellationToken);

                return updated > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Another instance holds a valid lease.
        return false;
    }
}
