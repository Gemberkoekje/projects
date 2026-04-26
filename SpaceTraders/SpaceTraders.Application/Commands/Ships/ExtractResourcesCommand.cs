using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record ExtractResourcesCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ExtractResourcesCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class ExtractResourcesHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IWaypointRepository waypoints,
    IMessageBus bus,
    ILogger<ExtractResourcesHandler> logger)
{
    public async Task Handle(ExtractResourcesCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (!string.Equals(ship?.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(ExtractResourcesCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit before extraction.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping extract for ship {Symbol}: expected IN_ORBIT but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return;
        }

        if (string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(ExtractResourcesCommand),
                "IN_ORBIT_AT_EXTRACTABLE_WAYPOINT",
                "IN_ORBIT_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate extraction location.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping extract for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
            return;
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        var waypointType = waypoint?.Type ?? string.Empty;
        var isExtractable = waypointType.Contains("ASTEROID", StringComparison.OrdinalIgnoreCase)
            || waypointType.Contains("ENGINEERED_ASTEROID", StringComparison.OrdinalIgnoreCase)
            || waypointType.Contains("DEBRIS_FIELD", StringComparison.OrdinalIgnoreCase);

        if (!isExtractable)
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(ExtractResourcesCommand),
                "IN_ORBIT_AT_EXTRACTABLE_WAYPOINT",
                $"IN_ORBIT_AT_{(string.IsNullOrWhiteSpace(waypointType) ? "UNKNOWN" : waypointType)}",
                "Ship is not at an extractable waypoint.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping extract for ship {Symbol}: waypoint {Waypoint} type {Type} is not extractable.", command.ShipSymbol, ship.WaypointSymbol, waypointType);
            return;
        }

        var result = await port.ExtractResourcesAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);
        logger.LogInformation("Ship {Symbol} extracted {Units}x {Good}. Cooldown: {Cooldown}s.",
            command.ShipSymbol, result.YieldUnits, result.YieldSymbol, result.CooldownSeconds);
    }
}
