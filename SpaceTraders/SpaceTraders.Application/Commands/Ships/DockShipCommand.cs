using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record DockShipCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public DockShipCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class DockShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<DockShipHandler> logger)
{
    public async Task Handle(DockShipCommand command, CancellationToken cancellationToken)
    {
        var nav = await port.DockShipAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, nav, null, cancellationToken);
        logger.LogInformation("Ship {Symbol} docked at {Waypoint}.", command.ShipSymbol, nav.WaypointSymbol);
    }
}
