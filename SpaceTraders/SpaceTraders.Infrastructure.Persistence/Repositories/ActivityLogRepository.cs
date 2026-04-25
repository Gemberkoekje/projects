using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

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

    public async Task<IReadOnlyList<ActivityLogDto>> GetPagedAsync(int page = 1, int pageSize = 50, string? shipFilter = null, CancellationToken cancellationToken = default)
    {
        var query = db.ActivityLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(shipFilter))
            query = query.Where(l => l.ShipSymbol == shipFilter);

        var items = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return items.Select(l => new ActivityLogDto(
            l.Id, l.Timestamp, l.ShipSymbol, l.EventType, l.Message, l.JsonDetails))
            .ToList();
    }

    public async Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        return await db.ActivityLogs
            .Where(l => l.Timestamp < olderThan)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
