using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record SiphonResourcesCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SiphonResourcesCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class SiphonResourcesHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IWaypointRepository waypoints,
    IMessageBus bus,
    ILogger<SiphonResourcesHandler> logger)
{
    public async Task Handle(SiphonResourcesCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (!string.Equals(ship?.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(SiphonResourcesCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit to siphon.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping siphon for ship {Symbol}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return;
        }

        if (ship is null || !ship.HasGasSiphonEquipment || !ship.HasGasProcessor)
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(SiphonResourcesCommand),
                "IN_ORBIT_WITH_GAS_SIPHON_AND_PROCESSOR",
                ship is null ? "UNKNOWN" : "IN_ORBIT_WITHOUT_REQUIRED_SIPHON_CAPABILITY",
                "Ship must have gas siphon equipment and a gas processor to siphon.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping siphon for ship {Symbol}: missing gas siphon or gas processor capability.", command.ShipSymbol);
            return;
        }

        if (string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(SiphonResourcesCommand),
                "IN_ORBIT_AT_GAS_GIANT",
                "IN_ORBIT_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate siphon location.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping siphon for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
            return;
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        if (waypoint is null || !waypoint.Type.Contains("GAS_GIANT", StringComparison.OrdinalIgnoreCase))
        {
            var actual = waypoint?.Type ?? "UNKNOWN";
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(SiphonResourcesCommand),
                "IN_ORBIT_AT_GAS_GIANT",
                $"IN_ORBIT_AT_{actual}",
                "Ship is not at a gas giant waypoint.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping siphon for ship {Symbol}: waypoint {Waypoint} type {Type} is not a gas giant.", command.ShipSymbol, ship.WaypointSymbol, actual);
            return;
        }

        var result = await port.SiphonResourcesAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);

        logger.LogInformation("Ship {Symbol} siphoned {Units}x {Symbol2}, cooldown {Seconds}s.",
            command.ShipSymbol, result.YieldUnits, result.YieldSymbol, result.CooldownSeconds);
    }
}
