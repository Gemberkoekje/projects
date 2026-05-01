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
}
