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
            // Expired lease – take it over.
            lease.HolderId = holderId;
            lease.ExpiresAt = now + leaseDuration;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        // Another instance holds a valid lease.
        return false;
    }
}
