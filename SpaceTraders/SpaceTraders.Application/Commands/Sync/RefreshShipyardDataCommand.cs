using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using System.Text.Json;
using Wolverine;

namespace SpaceTraders.Application.Commands.Sync;

public record RefreshShipyardDataCommand(string SystemSymbol, string WaypointSymbol);

public sealed class RefreshShipyardDataHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    IMessageBus bus,
    ILogger<RefreshShipyardDataHandler> logger)
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    public async Task Handle(RefreshShipyardDataCommand command, CancellationToken cancellationToken)
    {
        // Only refresh if a ship is present at this waypoint
        var shipPresent = await db.Ships
            .AsNoTracking()
            .AnyAsync(s => s.WaypointSymbol == command.WaypointSymbol && s.Status != "IN_TRANSIT", cancellationToken);

        if (!shipPresent)
        {
            logger.LogDebug("Skipping shipyard refresh for {Waypoint}: no ship present.", command.WaypointSymbol);
            return;
        }

        // Check TTL
        var existing = await db.Shipyards.FindAsync([command.WaypointSymbol], cancellationToken);
        if (existing is not null && existing.LastObservedAt + DefaultTtl > DateTimeOffset.UtcNow)
        {
            logger.LogDebug("Skipping shipyard refresh for {Waypoint}: data is fresh.", command.WaypointSymbol);
            return;
        }

        var shipyard = await apiClient.GetShipyardAsync(command.SystemSymbol, command.WaypointSymbol, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var shipTypesJson = shipyard.Ships is not null ? JsonSerializer.Serialize(shipyard.Ships) : null;

        if (existing is null)
        {
            db.Shipyards.Add(new CachedShipyard
            {
                WaypointSymbol = command.WaypointSymbol,
                SystemSymbol = command.SystemSymbol,
                ShipTypesJson = shipTypesJson,
                LastObservedAt = now
            });
        }
        else
        {
            existing.ShipTypesJson = shipTypesJson;
            existing.LastObservedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        await bus.PublishAsync(new ShipyardDataRefreshedEvent(new WaypointSymbol(command.WaypointSymbol)));
        logger.LogInformation("Shipyard data refreshed for {Waypoint}.", command.WaypointSymbol);
    }
}
