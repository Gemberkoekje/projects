using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;
using System.Text.Json;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ShipRepository(SpaceTradersDbContext db) : IShipRepository
{
    public async Task<ShipModel?> FindAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([db.AgentToken, symbol], cancellationToken);
        return entity is null ? null : MapToModel(entity);
    }

    public async Task<IReadOnlyList<ShipModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await db.Ships.AsNoTracking().OrderBy(s => s.Symbol).ToListAsync(cancellationToken);
        foreach (var e in entities)
            e.ApplyArrivalIfDue();
        return entities.Select(MapToModel).ToList();
    }

    public async Task<IReadOnlyList<ShipModel>> GetInTransitAsync(CancellationToken cancellationToken = default)
    {
        var entities = await db.Ships.Where(s => s.ArrivesAt.HasValue).ToListAsync(cancellationToken);
        return entities.Select(MapToModel).ToList();
    }

    public async Task<bool> IsShipAtWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default)
    {
        return await db.Ships.AsNoTracking()
            .AnyAsync(s => s.WaypointSymbol == waypointSymbol && s.Status != "IN_TRANSIT", cancellationToken);
    }

    public async Task UpsertAsync(ShipModel ship, CancellationToken cancellationToken = default)
    {
        var existing = await db.Ships.FindAsync([db.AgentToken, ship.Symbol], cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        var mountsJson = JsonSerializer.Serialize(ship.MountSymbols ?? []);
        var cargoJson = JsonSerializer.Serialize(ship.CargoInventory ?? []);

        if (existing is null)
        {
            db.Ships.Add(new CachedShip
            {
                AgentToken = db.AgentToken,
                Symbol = ship.Symbol,
                SystemSymbol = ship.SystemSymbol,
                WaypointSymbol = ship.WaypointSymbol,
                Status = ship.Status,
                FlightMode = ship.FlightMode,
                ShipType = ship.ShipType,
                MountsJson = mountsJson,
                ModulesJson = ship.ModulesJson,
                FrameJson = ship.FrameJson,
                ReactorJson = ship.ReactorJson,
                EngineJson = ship.EngineJson,
                CooldownExpiresAt = ship.CooldownExpiresAt,
                FuelCurrent = ship.FuelCurrent,
                FuelCapacity = ship.FuelCapacity,
                CargoCurrent = ship.CargoCurrent,
                CargoCapacity = ship.CargoCapacity,
                CargoJson = cargoJson,
                ArrivesAt = ship.ArrivesAt,
                DestWaypointSymbol = ship.DestWaypointSymbol,
                LastSyncedAt = now
            });
        }
        else
        {
            existing.SystemSymbol = ship.SystemSymbol;
            existing.WaypointSymbol = ship.WaypointSymbol;
            existing.Status = ship.Status;
            existing.FlightMode = ship.FlightMode;
            existing.ShipType = ship.ShipType;
            existing.MountsJson = mountsJson;
            existing.ModulesJson = ship.ModulesJson;
            existing.FrameJson = ship.FrameJson;
            existing.ReactorJson = ship.ReactorJson;
            existing.EngineJson = ship.EngineJson;
            existing.CooldownExpiresAt = ship.CooldownExpiresAt;
            existing.FuelCurrent = ship.FuelCurrent;
            existing.FuelCapacity = ship.FuelCapacity;
            existing.CargoCurrent = ship.CargoCurrent;
            existing.CargoCapacity = ship.CargoCapacity;
            existing.CargoJson = cargoJson;
            existing.ArrivesAt = ship.ArrivesAt;
            existing.DestWaypointSymbol = ship.DestWaypointSymbol;
            existing.LastSyncedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateNavAsync(string symbol, NavModel nav, FuelModel? fuel, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([db.AgentToken, symbol], cancellationToken);
        if (entity is null) return;

        entity.Status = nav.Status;
        entity.SystemSymbol = nav.SystemSymbol;
        entity.WaypointSymbol = nav.WaypointSymbol;
        entity.FlightMode = nav.FlightMode;
        entity.DestWaypointSymbol = nav.DestWaypointSymbol;
        entity.ArrivesAt = nav.ArrivesAt;
        entity.LastSyncedAt = TimeProvider.System.GetUtcNow();

        if (fuel is not null)
        {
            entity.FuelCurrent = fuel.Current;
            entity.FuelCapacity = fuel.Capacity;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCargoAsync(string symbol, CargoModel cargo, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([db.AgentToken, symbol], cancellationToken);
        if (entity is null) return;

        entity.CargoCurrent = cargo.Units;
        entity.CargoCapacity = cargo.Capacity;
        entity.CargoJson = JsonSerializer.Serialize(cargo.Inventory ?? []);
        entity.LastSyncedAt = TimeProvider.System.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateFuelAsync(string symbol, FuelModel fuel, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([db.AgentToken, symbol], cancellationToken);
        if (entity is null) return;

        entity.FuelCurrent = fuel.Current;
        entity.FuelCapacity = fuel.Capacity;
        entity.LastSyncedAt = TimeProvider.System.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ShipModel MapToModel(CachedShip entity)
    {
        var mounts = DeserializeMounts(entity.MountsJson);
        var cargoInventory = DeserializeCargo(entity.CargoJson);

        return new ShipModel(
            entity.Symbol,
            entity.SystemSymbol,
            entity.WaypointSymbol,
            entity.Status,
            entity.FlightMode,
            entity.FuelCurrent,
            entity.FuelCapacity,
            entity.ArrivesAt,
            entity.DestWaypointSymbol,
            entity.CargoCurrent,
            entity.CargoCapacity,
            entity.LastSyncedAt,
            entity.ShipType,
            mounts,
            cargoInventory,
            entity.ModulesJson,
            entity.FrameJson,
            entity.ReactorJson,
            entity.EngineJson,
            entity.CooldownExpiresAt);
    }

    private static IReadOnlyList<string> DeserializeMounts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<CargoItemModel> DeserializeCargo(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CargoItemModel>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
