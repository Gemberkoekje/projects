using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.Application.Commands.Ships;

public record OrbitShipCommand(string ShipSymbol);

public sealed class OrbitShipHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    ILogger<OrbitShipHandler> logger)
{
    public async Task Handle(OrbitShipCommand command, CancellationToken cancellationToken)
    {
        var result = await apiClient.OrbitShipAsync(command.ShipSymbol, cancellationToken);

        var cached = await db.Ships.FindAsync([command.ShipSymbol], cancellationToken);
        if (cached is not null)
        {
            cached.Status = result.Nav.Status;
            cached.WaypointSymbol = result.Nav.WaypointSymbol;
            cached.SystemSymbol = result.Nav.SystemSymbol;
            cached.ArrivesAt = null;
            cached.DestWaypointSymbol = null;
            cached.LastSyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Ship {Symbol} entered orbit at {Waypoint}.", command.ShipSymbol, result.Nav.WaypointSymbol);
    }
}
