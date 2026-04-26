using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record NavigateShipCommand
{
    public required string ShipSymbol { get; init; }

    public required string DestinationWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NavigateShipCommand(string ShipSymbol, string DestinationWaypoint)
    {
        this.ShipSymbol = ShipSymbol;
        this.DestinationWaypoint = DestinationWaypoint;
    }
}

public sealed class NavigateShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<NavigateShipHandler> logger)
{
    public async Task Handle(NavigateShipCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (ship?.Status?.Equals("DOCKED", StringComparison.OrdinalIgnoreCase) == true)
        {
            var orbitNav = await port.OrbitShipAsync(command.ShipSymbol, cancellationToken);
            await ships.UpdateNavAsync(command.ShipSymbol, orbitNav, null, cancellationToken);
            logger.LogInformation("Ship {Symbol} was docked and moved to orbit before navigation.", command.ShipSymbol);
        }

        var result = await port.NavigateShipAsync(command.ShipSymbol, command.DestinationWaypoint, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, result.Nav, result.Fuel, cancellationToken);

        var destinationSystem = ExtractSystemSymbol(command.DestinationWaypoint);
        await bus.SendAsync(new RefreshSystemDataCommand(destinationSystem));

        logger.LogInformation("Ship {Symbol} navigating to {Waypoint}, arrives at {Arrival}.",
            command.ShipSymbol, command.DestinationWaypoint, result.Nav.ArrivesAt);
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}
