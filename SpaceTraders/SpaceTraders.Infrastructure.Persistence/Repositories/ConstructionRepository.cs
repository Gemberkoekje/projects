using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ConstructionRepository(SpaceTradersDbContext db) : IConstructionRepository
{
    public async Task<ConstructionSiteModel?> FindAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.ConstructionSites
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.WaypointSymbol == waypointSymbol, cancellationToken);

        return entity is null ? null : MapToModel(entity);
    }

    public async Task<IReadOnlyList<ConstructionSiteModel>> GetIncompleteAsync(CancellationToken cancellationToken = default)
    {
        var entities = await db.ConstructionSites
            .AsNoTracking()
            .Where(c => !c.IsComplete)
            .OrderBy(c => c.WaypointSymbol)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToModel).ToList();
    }

    public async Task UpsertAsync(ConstructionSiteModel site, string systemSymbol, CancellationToken cancellationToken = default)
    {
        var existing = await db.ConstructionSites
            .FirstOrDefaultAsync(c => c.WaypointSymbol == site.WaypointSymbol, cancellationToken);

        var materialsJson = JsonSerializer.Serialize(site.Materials);
        var now = TimeProvider.System.GetUtcNow();

        if (existing is not null)
        {
            existing.IsComplete = site.IsComplete;
            existing.MaterialsJson = materialsJson;
            existing.LastObservedAt = now;
        }
        else
        {
            db.ConstructionSites.Add(new CachedConstruction
            {
                AgentToken = db.AgentToken,
                WaypointSymbol = site.WaypointSymbol,
                SystemSymbol = systemSymbol,
                IsComplete = site.IsComplete,
                MaterialsJson = materialsJson,
                LastObservedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ConstructionSiteModel MapToModel(CachedConstruction entity)
    {
        var materials = TryDeserializeMaterials(entity.MaterialsJson);
        return new ConstructionSiteModel(entity.WaypointSymbol, entity.IsComplete, materials);
    }

    private static IReadOnlyList<ConstructionMaterialModel> TryDeserializeMaterials(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ConstructionMaterialModel>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
