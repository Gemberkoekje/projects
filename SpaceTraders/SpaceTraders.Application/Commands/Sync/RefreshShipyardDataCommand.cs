using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Commands.Sync;

public record RefreshShipyardDataCommand(string SystemSymbol, string WaypointSymbol);

public sealed class RefreshShipyardDataHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IShipyardRepository shipyards,
    IMessageBus bus,
    ILogger<RefreshShipyardDataHandler> logger)
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    public async Task Handle(RefreshShipyardDataCommand command, CancellationToken cancellationToken)
    {
        if (!await ships.IsShipAtWaypointAsync(command.WaypointSymbol, cancellationToken))
        {
            logger.LogDebug("Skipping shipyard refresh for {Waypoint}: no ship present.", command.WaypointSymbol);
            return;
        }

        var lastObserved = await shipyards.GetLastObservedAtAsync(command.WaypointSymbol, cancellationToken);
        if (lastObserved.HasValue && lastObserved.Value + DefaultTtl > DateTimeOffset.UtcNow)
        {
            logger.LogDebug("Skipping shipyard refresh for {Waypoint}: data is fresh.", command.WaypointSymbol);
            return;
        }

        var shipyard = await port.GetShipyardAsync(command.SystemSymbol, command.WaypointSymbol, cancellationToken);
        await shipyards.UpsertAsync(shipyard, cancellationToken);
        await bus.PublishAsync(new ShipyardDataRefreshedEvent(new WaypointSymbol(command.WaypointSymbol)));
        logger.LogInformation("Shipyard data refreshed for {Waypoint}.", command.WaypointSymbol);
    }
}
