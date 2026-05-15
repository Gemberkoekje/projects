using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ShipyardRepository(SpaceTradersDbContext db) : IShipyardRepository
{
    public async Task<DateTimeOffset?> GetLastObservedAtAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Shipyards.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WaypointSymbol == waypointSymbol, cancellationToken);
        return entity?.LastObservedAt;
    }

    public async Task<string?> FindShipyardForTypeAsync(string shipType, CancellationToken cancellationToken = default)
    {
        var shipyards = await db.Shipyards
            .AsNoTracking()
            .Where(s => s.AgentToken == db.AgentToken)
            .Where(s => s.ShipTypesJson != null && s.ShipTypesJson.Contains(shipType))
            .OrderByDescending(s => s.LastObservedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return shipyards?.WaypointSymbol;
    }

    public async Task UpsertAsync(ShipyardDataModel shipyard, CancellationToken cancellationToken = default)
    {
        var existing = await db.Shipyards.FindAsync([db.AgentToken, shipyard.WaypointSymbol], cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        var values = new CachedShipyard
        {
            AgentToken = db.AgentToken,
            WaypointSymbol = shipyard.WaypointSymbol,
            SystemSymbol = shipyard.SystemSymbol,
            ShipTypesJson = shipyard.ShipTypesJson,
            ShipsDetailJson = shipyard.ShipsDetailJson,
            LastObservedAt = now
        };

        if (existing is null)
        {
            db.Shipyards.Add(values);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(values);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShipyardWaypointDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var shipyards = await db.Shipyards
            .AsNoTracking()
            .Where(s => s.AgentToken == db.AgentToken)
            .ToListAsync(cancellationToken);

        return shipyards.Select(s => MapToDto(s)).ToList();
    }

    public async Task<ShipyardWaypointDto?> FindByWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Shipyards
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.WaypointSymbol == waypointSymbol && s.AgentToken == db.AgentToken, cancellationToken);

        return entity is null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ShipyardFreshnessDto>> GetAllFreshnessAsync(CancellationToken cancellationToken = default)
    {
        var shipyards = await db.Shipyards
            .AsNoTracking()
            .Where(s => s.AgentToken == db.AgentToken)
            .Select(s => new ShipyardFreshnessDto
            {
                WaypointSymbol = s.WaypointSymbol,
                SystemSymbol = s.SystemSymbol,
                LastObservedAt = s.LastObservedAt,
                AgeMinutes = 0
            })
            .ToListAsync(cancellationToken);

        return shipyards;
    }

    private static ShipyardWaypointDto MapToDto(CachedShipyard entity)
    {
        var shipTypes = string.IsNullOrEmpty(entity.ShipTypesJson)
            ? Array.Empty<string>()
            : DeserializeShipTypes(entity.ShipTypesJson);

        var ships = string.IsNullOrEmpty(entity.ShipsDetailJson)
            ? Array.Empty<ShipyardShipDto>()
            : DeserializeShips(entity.ShipsDetailJson);

        return new ShipyardWaypointDto
        {
            WaypointSymbol = entity.WaypointSymbol,
            SystemSymbol = entity.SystemSymbol,
            ShipTypes = shipTypes,
            Ships = ships
        };
    }

    private static string[] DeserializeShipTypes(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var shipTypes = new List<string>();
            var arrayLength = root.GetArrayLength();

            for (var i = 0; i < arrayLength; i++)
            {
                var element = root[i];

                if (element.ValueKind == JsonValueKind.String)
                {
                    var shipType = element.GetString();
                    if (!string.IsNullOrWhiteSpace(shipType))
                    {
                        shipTypes.Add(shipType);
                    }

                    continue;
                }

                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("type", out var typeProperty)
                    && typeProperty.ValueKind == JsonValueKind.String)
                {
                    var shipType = typeProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(shipType))
                    {
                        shipTypes.Add(shipType);
                    }
                }
            }

            return shipTypes.ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static ShipyardShipDto[] DeserializeShips(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ShipyardShipDto>();
        }

        var ships = new List<ShipyardShipDto>();
        var arrayLength = root.GetArrayLength();
        for (var i = 0; i < arrayLength; i++)
        {
            var element = root[i];
            ships.Add(new ShipyardShipDto
            {
                Type = element.GetProperty("type").GetString() ?? string.Empty,
                Name = element.TryGetProperty("name", out var name) ? name.GetString() : null,
                Description = element.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                Supply = element.TryGetProperty("supply", out var supply) ? supply.GetString() : null,
                Activity = element.TryGetProperty("activity", out var activity) ? activity.GetString() : null,
                PurchasePrice = element.TryGetProperty("purchasePrice", out var price) ? price.GetInt64() : 0
            });
        }

        return ships.ToArray();
    }
}
