using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Persists a ship task record whenever a ship is assigned to a new task.
/// </summary>
public sealed class ShipTaskRecordHandler(IShipTaskRecordRepository shipTaskRecords)
{
    public async Task Handle(ShipAssignedEvent @event, CancellationToken cancellationToken)
    {
        await shipTaskRecords.StartTaskAsync(
            @event.ShipSymbol,
            @event.AssignmentType,
            cancellationToken: cancellationToken);
    }
}
