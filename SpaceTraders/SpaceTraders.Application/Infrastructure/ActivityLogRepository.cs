using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Application.Repositories;

public sealed class ActivityLogRepository(SpaceTradersDbContext db) : IActivityLogRepository
{
    public async Task AppendAsync(string shipSymbol, string eventType, string message, string? jsonDetails = null, CancellationToken cancellationToken = default)
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            ShipSymbol = shipSymbol,
            EventType = eventType,
            Message = message,
            JsonDetails = jsonDetails
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
