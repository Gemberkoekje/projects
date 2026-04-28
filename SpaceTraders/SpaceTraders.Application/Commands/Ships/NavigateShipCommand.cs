using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
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
        if (!string.Equals(ship?.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(NavigateShipCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit before navigation.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning(
                "Skipping navigation for ship {Symbol} to {Waypoint}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol,
                command.DestinationWaypoint,
                ship?.Status ?? "UNKNOWN");
            return;
        }

        var nowNavigate = TimeProvider.System.GetUtcNow();
        var result = await port.NavigateShipAsync(command.ShipSymbol, command.DestinationWaypoint, cancellationToken);
        await ships.UpdateNavAsync(command.ShipSymbol, result.Nav, result.Fuel, cancellationToken);

        var inTransitEvent = new ShipInTransitEvent(
            command.ShipSymbol,
            ship?.WaypointSymbol ?? result.Nav.WaypointSymbol,
            command.DestinationWaypoint,
            result.Nav.ArrivesAt ?? nowNavigate,
            Guid.Empty,
            Guid.Empty,
            nowNavigate);

        await bus.PublishAsync(inTransitEvent);

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
