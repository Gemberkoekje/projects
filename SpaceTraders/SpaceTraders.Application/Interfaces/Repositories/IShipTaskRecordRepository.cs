namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IShipTaskRecordRepository
{
    Task StartTaskAsync(
        string shipSymbol,
        string taskKind,
        string? targetWaypoint = null,
        string? payloadJson = null,
        CancellationToken cancellationToken = default);

    Task EndCurrentTaskAsync(string shipSymbol, CancellationToken cancellationToken = default);

    /// <summary>Deletes all ship task records with <c>StartedAt</c> older than <paramref name="olderThan"/>.</summary>
    /// <returns>Number of rows deleted.</returns>
    Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
