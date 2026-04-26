using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record OrbitShipCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public OrbitShipCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class OrbitShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<OrbitShipHandler> logger)
{
    public async Task Handle(OrbitShipCommand command, CancellationToken cancellationToken)
    {
        var nav = await port.OrbitShipAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, nav, null, cancellationToken);

        if (!string.IsNullOrWhiteSpace(nav.WaypointSymbol))
        {
            await bus.PublishAsync(new ShipEnteredOrbitEvent(command.ShipSymbol, new WaypointSymbol(nav.WaypointSymbol)));
        }

        logger.LogInformation("Ship {Symbol} in orbit at {Waypoint}.", command.ShipSymbol, nav.WaypointSymbol);
    }
}
