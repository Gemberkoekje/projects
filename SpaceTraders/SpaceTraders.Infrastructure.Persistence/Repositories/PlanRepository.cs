using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class PlanRepository(SpaceTradersDbContext db) : IPlanRepository
{
    public async Task<TPlan?> GetAsync<TPlan>(string planType, CancellationToken cancellationToken = default)
        where TPlan : class
    {
        var entity = await db.PlanStates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.PlanType == planType, cancellationToken);

        if (entity is null)
        {
            return default;
        }

        return Deserialize<TPlan>(entity.StateJson);
    }

    public async Task UpsertAsync<TPlan>(string planType, TPlan state, CancellationToken cancellationToken = default)
        where TPlan : class
    {
        var entity = await db.PlanStates
            .SingleOrDefaultAsync(x => x.PlanType == planType, cancellationToken);

        if (entity is null)
        {
            db.PlanStates.Add(new PlanStateRecord
            {
                AgentToken = db.AgentToken,
                PlanType = planType,
                StateJson = JsonSerializer.Serialize(state),
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            entity.StateJson = JsonSerializer.Serialize(state);
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string planType, CancellationToken cancellationToken = default)
    {
        var entity = await db.PlanStates
            .SingleOrDefaultAsync(x => x.PlanType == planType, cancellationToken);

        if (entity is null)
        {
            return;
        }

        db.PlanStates.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static TPlan? Deserialize<TPlan>(string stateJson)
        where TPlan : class
    {
        try
        {
            return JsonSerializer.Deserialize<TPlan>(stateJson);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
