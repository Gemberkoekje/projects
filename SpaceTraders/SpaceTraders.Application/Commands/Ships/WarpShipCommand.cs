using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record WarpShipCommand
{
    public required string ShipSymbol { get; init; }

    public required string DestinationWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public WarpShipCommand(string ShipSymbol, string DestinationWaypoint)
    {
        this.ShipSymbol = ShipSymbol;
        this.DestinationWaypoint = DestinationWaypoint;
    }
}

public sealed class WarpShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<WarpShipHandler> logger)
{
    public async Task Handle(WarpShipCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (!string.Equals(ship?.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(WarpShipCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit to warp.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping warp for ship {Symbol}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return;
        }

        var nowWarp = TimeProvider.System.GetUtcNow();
        var result = await port.WarpShipAsync(command.ShipSymbol, command.DestinationWaypoint, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, result.Nav, result.Fuel, cancellationToken);

        var inTransitEvent = new ShipInTransitEvent(
            command.ShipSymbol,
            ship?.WaypointSymbol ?? result.Nav.WaypointSymbol,
            command.DestinationWaypoint,
            result.Nav.ArrivesAt ?? nowWarp,
            Guid.Empty,
            Guid.Empty,
            nowWarp);

        await bus.PublishAsync(inTransitEvent);

        logger.LogInformation("Ship {Symbol} warping to {Waypoint}, arrives at {Arrival}.",
            command.ShipSymbol, command.DestinationWaypoint, result.Nav.ArrivesAt);
    }
}
