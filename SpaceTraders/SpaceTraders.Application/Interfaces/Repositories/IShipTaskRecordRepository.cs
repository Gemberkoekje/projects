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

    /// <summary>
    /// Returns ship task records for <paramref name="shipSymbol"/> in the given time range,
    /// ordered by <c>StartedAt</c> descending.
    /// </summary>
    Task<IReadOnlyList<ShipTaskRecordDto>> GetTimelineAsync(
        string shipSymbol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all ship task records across all ships whose <c>StartedAt</c> falls within
    /// [<paramref name="from"/>, <paramref name="to"/>], ordered by <c>StartedAt</c> ascending.
    /// </summary>
    Task<IReadOnlyList<ShipTaskRecordDto>> GetAllInTimeWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record ShipTaskRecordDto(
    long Id,
    string ShipSymbol,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string TaskKind,
    string? TargetWaypoint,
    string? PayloadJson);
