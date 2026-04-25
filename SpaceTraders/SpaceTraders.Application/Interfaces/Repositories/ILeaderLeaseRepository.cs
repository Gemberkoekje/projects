namespace SpaceTraders.Application.Interfaces.Repositories;

/// <summary>
/// Repository for the leader election lease record.
/// </summary>
public interface ILeaderLeaseRepository
{
    /// <summary>
    /// Tries to acquire or renew the named lease for the given holder.
    /// Returns <c>true</c> when this instance successfully owns the lease after the call.
    /// </summary>
    Task<bool> TryAcquireOrRenewAsync(
        string leaseKey,
        string holderId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
}
