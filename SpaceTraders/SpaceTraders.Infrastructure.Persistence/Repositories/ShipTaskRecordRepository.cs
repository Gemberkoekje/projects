using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ShipTaskRecordRepository(SpaceTradersDbContext db) : IShipTaskRecordRepository
{
    public async Task StartTaskAsync(
        string shipSymbol,
        string taskKind,
        string? targetWaypoint = null,
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();

        // Close any currently open task for this ship
        var openTask = await db.ShipTaskRecords
            .Where(r => r.ShipSymbol == shipSymbol && r.EndedAt == null)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (openTask is not null)
        {
            openTask.EndedAt = now;
            db.ShipTaskRecords.Update(openTask);
        }

        var record = new ShipTaskRecord
        {
            AgentToken = db.AgentToken,
            ShipSymbol = shipSymbol,
            StartedAt = now,
            TaskKind = taskKind,
            TargetWaypoint = targetWaypoint,
            PayloadJson = payloadJson,
        };

        db.ShipTaskRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task EndCurrentTaskAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var openTask = await db.ShipTaskRecords
            .Where(r => r.ShipSymbol == shipSymbol && r.EndedAt == null)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (openTask is null)
        {
            return;
        }

        openTask.EndedAt = TimeProvider.System.GetUtcNow();
        db.ShipTaskRecords.Update(openTask);
        await db.SaveChangesAsync(cancellationToken);
    }
}
