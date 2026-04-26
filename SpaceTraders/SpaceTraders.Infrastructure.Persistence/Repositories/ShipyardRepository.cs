using Microsoft.EntityFrameworkCore;
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
}
