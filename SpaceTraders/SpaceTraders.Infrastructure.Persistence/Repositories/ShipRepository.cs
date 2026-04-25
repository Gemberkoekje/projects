using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ShipRepository(SpaceTradersDbContext db) : IShipRepository
{
    public async Task<ShipModel?> FindAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([symbol], cancellationToken);
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
        var existing = await db.Ships.FindAsync([ship.Symbol], cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            db.Ships.Add(new CachedShip
            {
                Symbol = ship.Symbol,
                SystemSymbol = ship.SystemSymbol,
                WaypointSymbol = ship.WaypointSymbol,
                Status = ship.Status,
                FlightMode = ship.FlightMode,
                FuelCurrent = ship.FuelCurrent,
                FuelCapacity = ship.FuelCapacity,
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
            existing.FuelCurrent = ship.FuelCurrent;
            existing.FuelCapacity = ship.FuelCapacity;
            existing.ArrivesAt = ship.ArrivesAt;
            existing.DestWaypointSymbol = ship.DestWaypointSymbol;
            existing.LastSyncedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateNavAsync(string symbol, NavModel nav, FuelModel? fuel, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([symbol], cancellationToken);
        if (entity is null) return;

        entity.Status = nav.Status;
        entity.SystemSymbol = nav.SystemSymbol;
        entity.WaypointSymbol = nav.WaypointSymbol;
        entity.FlightMode = nav.FlightMode;
        entity.DestWaypointSymbol = nav.DestWaypointSymbol;
        entity.ArrivesAt = nav.ArrivesAt;
        entity.LastSyncedAt = DateTimeOffset.UtcNow;

        if (fuel is not null)
        {
            entity.FuelCurrent = fuel.Current;
            entity.FuelCapacity = fuel.Capacity;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCargoAsync(string symbol, CargoModel cargo, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([symbol], cancellationToken);
        if (entity is null) return;

        entity.CargoCurrent = cargo.Units;
        entity.CargoCapacity = cargo.Capacity;
        entity.LastSyncedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateFuelAsync(string symbol, FuelModel fuel, CancellationToken cancellationToken = default)
    {
        var entity = await db.Ships.FindAsync([symbol], cancellationToken);
        if (entity is null) return;

        entity.FuelCurrent = fuel.Current;
        entity.FuelCapacity = fuel.Capacity;
        entity.LastSyncedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ShipModel MapToModel(CachedShip entity) =>
        new(
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
            entity.LastSyncedAt);
}
