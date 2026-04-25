using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships;

public record NavigateShipCommand(string ShipSymbol, string DestinationWaypoint);

public sealed class NavigateShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<NavigateShipHandler> logger)
{
    public async Task Handle(NavigateShipCommand command, CancellationToken cancellationToken)
    {
        var result = await port.NavigateShipAsync(command.ShipSymbol, command.DestinationWaypoint, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, result.Nav, result.Fuel, cancellationToken);
        logger.LogInformation("Ship {Symbol} navigating to {Waypoint}, arrives at {Arrival}.",
            command.ShipSymbol, command.DestinationWaypoint, result.Nav.ArrivesAt);
    }
}
