using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.Application.Commands.Ships;

public record NavigateShipCommand(string ShipSymbol, string DestinationWaypoint);

public sealed class NavigateShipHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    ILogger<NavigateShipHandler> logger)
{
    public async Task Handle(NavigateShipCommand command, CancellationToken cancellationToken)
    {
        var result = await apiClient.NavigateShipAsync(command.ShipSymbol, command.DestinationWaypoint, cancellationToken);

        var cached = await db.Ships.FindAsync([command.ShipSymbol], cancellationToken);
        if (cached is not null)
        {
            cached.Status = result.Nav.Status;
            cached.WaypointSymbol = result.Nav.WaypointSymbol;
            cached.SystemSymbol = result.Nav.SystemSymbol;
            cached.DestWaypointSymbol = result.Nav.Route?.Destination?.Symbol;
            cached.ArrivesAt = result.Nav.Route?.Arrival;
            if (result.Fuel is not null)
            {
                cached.FuelCurrent = result.Fuel.Current;
                cached.FuelCapacity = result.Fuel.Capacity;
            }
            cached.LastSyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Ship {Symbol} navigating to {Waypoint}, arrives at {Arrival}.",
            command.ShipSymbol, command.DestinationWaypoint, result.Nav.Route?.Arrival);
    }
}
