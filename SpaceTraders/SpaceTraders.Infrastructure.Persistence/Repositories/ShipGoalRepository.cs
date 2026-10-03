using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stores and retrieves the active <see cref="ShipGoal"/> for each ship using the
/// <c>GoalId</c>, <c>GoalKind</c>, <c>GoalPayloadJson</c>, and <c>GoalStatus</c> columns
/// on the <c>cached_ships</c> table.
/// </summary>
/// <remarks>Phase 8: introduced as part of the goal-driven architecture migration.</remarks>
public sealed class ShipGoalRepository(SpaceTradersDbContext db) : IShipGoalRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task<ShipGoal?> GetActiveGoalAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([db.AgentId, shipSymbol], cancellationToken);
        if (entity?.GoalPayloadJson is null)
        {
            return null;
        }

        var goal = JsonSerializer.Deserialize<ShipGoal>(entity.GoalPayloadJson, JsonOptions);
        if (goal is not null && entity.GoalStatus.HasValue)
        {
            // The GoalStatus column is the authoritative status; it may differ from the JSON
            // when UpdateGoalStatusAsync was called after SetActiveGoalAsync.
            goal = goal with { Status = (GoalStatus)entity.GoalStatus.Value };
        }

        return goal;
    }

    public async Task SetActiveGoalAsync(string shipSymbol, ShipGoal goal, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([db.AgentId, shipSymbol], cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.GoalId = goal.GoalId;
        entity.GoalKind = goal.Kind.ToString();
        entity.GoalPayloadJson = JsonSerializer.Serialize(goal, JsonOptions);
        entity.GoalStatus = (int)goal.Status;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearActiveGoalAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([db.AgentId, shipSymbol], cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.GoalId = null;
        entity.GoalKind = null;
        entity.GoalPayloadJson = null;
        entity.GoalStatus = null;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateGoalStatusAsync(string shipSymbol, Guid goalId, GoalStatus status, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([db.AgentId, shipSymbol], cancellationToken);
        if (entity is null || entity.GoalId != goalId)
        {
            return;
        }

        entity.GoalStatus = (int)status;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task BlockGoalAsync(string shipSymbol, Guid goalId, string reason, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([db.AgentId, shipSymbol], cancellationToken);
        if (entity?.GoalPayloadJson is null || entity.GoalId != goalId)
        {
            return;
        }

        var goal = JsonSerializer.Deserialize<ShipGoal>(entity.GoalPayloadJson, JsonOptions);
        if (goal is null)
        {
            return;
        }

        var blocked = goal with { Status = GoalStatus.Blocked, StatusReason = reason };
        entity.GoalPayloadJson = JsonSerializer.Serialize(blocked, JsonOptions);
        entity.GoalStatus = (int)GoalStatus.Blocked;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetActiveScoutTargetsAsync(CancellationToken cancellationToken = default)
    {
        var payloads = await db.Ships
            .Where(s => s.GoalKind == "ScoutWaypoint" && s.GoalPayloadJson != null)
            .Select(s => s.GoalPayloadJson)
            .ToListAsync(cancellationToken);

        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in payloads)
        {
            if (json is null)
            {
                continue;
            }

            var goal = JsonSerializer.Deserialize<ShipGoal>(json, JsonOptions);
            if (goal is ScoutWaypointGoal scout && !string.IsNullOrWhiteSpace(scout.TargetWaypointSymbol))
            {
                targets.Add(scout.TargetWaypointSymbol);
            }
        }

        return targets;
    }

    public async Task<IReadOnlyDictionary<string, TradeBetweenMarketsGoal>> GetActiveTradeGoalsAsync(CancellationToken cancellationToken = default)
    {
        var kind = ShipGoalKind.TradeBetweenMarkets.ToString();
        var rows = await db.Ships
            .Where(s => s.GoalKind == kind && s.GoalPayloadJson != null)
            .Select(s => new { s.Symbol, s.GoalPayloadJson, s.GoalStatus })
            .ToListAsync(cancellationToken);

        var trips = new Dictionary<string, TradeBetweenMarketsGoal>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.GoalPayloadJson is not null
                && JsonSerializer.Deserialize<ShipGoal>(row.GoalPayloadJson, JsonOptions) is TradeBetweenMarketsGoal trip)
            {
                // The GoalStatus column is the authoritative status, as in GetActiveGoalAsync.
                trips[row.Symbol] = row.GoalStatus.HasValue ? trip with { Status = (GoalStatus)row.GoalStatus.Value } : trip;
            }
        }

        return trips;
    }
}
