using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.Application.Sync;

public sealed class SyncAllShipsHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    ILogger<SyncAllShipsHandler> logger)
{
    public async Task Handle(SyncAllShipsCommand command, CancellationToken cancellationToken)
    {
        var page = 1;
        const int limit = 20;
        var now = DateTimeOffset.UtcNow;
        var total = 0;

        while (true)
        {
            var response = await apiClient.GetMyShipsAsync(page, limit, cancellationToken);

            foreach (var ship in response.Data)
            {
                var arrivesAt = ship.Nav?.Route?.Arrival;
                var cached = await db.Ships.FindAsync(new object[] { ship.Symbol }, cancellationToken);

                if (cached is null)
                {
                    db.Ships.Add(new CachedShip
                    {
                        Symbol = ship.Symbol,
                        SystemSymbol = ship.Nav?.SystemSymbol,
                        WaypointSymbol = ship.Nav?.WaypointSymbol,
                        Status = ship.Nav?.Status,
                        FlightMode = ship.Nav?.FlightMode,
                        FuelCurrent = ship.Fuel?.Current ?? 0,
                        FuelCapacity = ship.Fuel?.Capacity ?? 0,
                        ArrivesAt = arrivesAt,
                        LastSyncedAt = now
                    });
                }
                else
                {
                    cached.SystemSymbol = ship.Nav?.SystemSymbol;
                    cached.WaypointSymbol = ship.Nav?.WaypointSymbol;
                    cached.Status = ship.Nav?.Status;
                    cached.FlightMode = ship.Nav?.FlightMode;
                    cached.FuelCurrent = ship.Fuel?.Current ?? 0;
                    cached.FuelCapacity = ship.Fuel?.Capacity ?? 0;
                    cached.ArrivesAt = arrivesAt;
                    cached.LastSyncedAt = now;
                }
            }

            total += response.Data.Count;

            if (response.Data.Count == 0 || total >= response.Meta.Total)
                break;

            page++;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Synced {Count} ships.", total);
    }
}
