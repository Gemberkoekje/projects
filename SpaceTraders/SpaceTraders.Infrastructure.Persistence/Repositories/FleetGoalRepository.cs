using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stores and retrieves <see cref="FleetGoal"/> instances in the <c>fleet_goals</c> table.
/// </summary>
/// <remarks>Phase 11e: introduced as part of the goal-driven architecture migration.</remarks>
public sealed class FleetGoalRepository(SpaceTradersDbContext db) : IFleetGoalRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task UpsertAsync(FleetGoal goal, CancellationToken ct = default)
    {
        var payloadJson = JsonSerializer.Serialize(goal, JsonOptions);

        var existing = await db.FleetGoals
            .FirstOrDefaultAsync(x => x.Id == goal.Id, ct);

        if (existing is null)
        {
            db.FleetGoals.Add(new FleetGoalRecord
            {
                Id = goal.Id,
                AgentToken = db.AgentToken,
                Kind = goal.Kind.ToString(),
                Priority = goal.Priority,
                Description = goal.Description,
                PayloadJson = payloadJson,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Kind = goal.Kind.ToString();
            existing.Priority = goal.Priority;
            existing.Description = goal.Description;
            existing.PayloadJson = payloadJson;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkCompletedAsync(Guid goalId, CancellationToken ct = default)
    {
        var entity = await db.FleetGoals
            .FirstOrDefaultAsync(x => x.Id == goalId, ct);

        if (entity is null)
        {
            return;
        }

        entity.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FleetGoal>> GetActiveAsync(CancellationToken ct = default)
    {
        var entities = await db.FleetGoals
            .AsNoTracking()
            .Where(x => x.CompletedAt == null)
            .ToListAsync(ct);

        var goals = new List<FleetGoal>(entities.Count);
        foreach (var entity in entities)
        {
            var goal = DeserializeGoal(entity.PayloadJson);
            if (goal is not null)
            {
                goals.Add(goal);
            }
        }

        return goals;
    }

    private static FleetGoal? DeserializeGoal(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<FleetGoal>(payloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
