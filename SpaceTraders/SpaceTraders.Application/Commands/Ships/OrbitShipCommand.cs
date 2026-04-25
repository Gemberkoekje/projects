using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships;

public record OrbitShipCommand(string ShipSymbol);

public sealed class OrbitShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<OrbitShipHandler> logger)
{
    public async Task Handle(OrbitShipCommand command, CancellationToken cancellationToken)
    {
        var nav = await port.OrbitShipAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, nav, null, cancellationToken);
        logger.LogInformation("Ship {Symbol} in orbit at {Waypoint}.", command.ShipSymbol, nav.WaypointSymbol);
    }
}
