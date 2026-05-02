using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persists and queries the per-ship goal history in the <c>ship_goal_history</c> table.
/// </summary>
/// <remarks>Phase 17e: introduced as part of the fleet dashboard implementation.</remarks>
public sealed class ShipGoalHistoryRepository(SpaceTradersDbContext db) : IShipGoalHistoryRepository
{
    public async Task AppendAsync(ShipGoalHistoryEntry entry, CancellationToken ct = default)
    {
        db.ShipGoalHistory.Add(new ShipGoalHistoryRecord
        {
            Id = entry.Id,
            AgentToken = db.AgentToken,
            ShipSymbol = entry.ShipSymbol,
            GoalKind = entry.GoalKind,
            GoalId = entry.GoalId,
            Outcome = entry.Outcome,
            Reason = entry.Reason,
            StartedAt = entry.StartedAt,
            EndedAt = entry.EndedAt,
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ShipGoalHistoryEntry>> GetRecentAsync(
        string shipSymbol,
        int limit,
        CancellationToken ct = default)
    {
        var records = await db.ShipGoalHistory
            .AsNoTracking()
            .Where(r => r.ShipSymbol == shipSymbol)
            .OrderByDescending(r => r.EndedAt)
            .Take(limit)
            .ToListAsync(ct);

        return records
            .Select(r => new ShipGoalHistoryEntry
            {
                Id = r.Id,
                ShipSymbol = r.ShipSymbol,
                GoalKind = r.GoalKind,
                GoalId = r.GoalId,
                Outcome = r.Outcome,
                Reason = r.Reason,
                StartedAt = r.StartedAt,
                EndedAt = r.EndedAt,
            })
            .ToList();
    }
}
