using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record InstallMountCommand
{
    public required string ShipSymbol { get; init; }

    public required string MountSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public InstallMountCommand(string ShipSymbol, string MountSymbol)
    {
        this.ShipSymbol = ShipSymbol;
        this.MountSymbol = MountSymbol;
    }
}

public sealed class InstallMountHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    IWaypointRepository waypoints,
    IMessageBus bus,
    ILogger<InstallMountHandler> logger)
{
    public async Task Handle(InstallMountCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (!string.Equals(ship?.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(InstallMountCommand),
                "DOCKED_AT_SHIPYARD",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked at a shipyard waypoint before installing a mount.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping install mount for ship {Symbol}: expected DOCKED but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return;
        }

        if (string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(InstallMountCommand),
                "DOCKED_AT_SHIPYARD",
                "DOCKED_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate shipyard.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping install mount for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
            return;
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        if (waypoint?.HasShipyard != true)
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(InstallMountCommand),
                "DOCKED_AT_SHIPYARD",
                "DOCKED_AT_NON_SHIPYARD_WAYPOINT",
                "Ship is not docked at a shipyard waypoint.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping install mount for ship {Symbol}: waypoint {Waypoint} is not a shipyard.", command.ShipSymbol, ship.WaypointSymbol);
            return;
        }

        var result = await port.InstallMountAsync(command.ShipSymbol, command.MountSymbol, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);

        var updated = ship with
        {
            MountSymbols = result.MountSymbols,
            CargoCurrent = result.Cargo.Units,
            CargoCapacity = result.Cargo.Capacity,
            CargoInventory = result.Cargo.Inventory,
        };
        await ships.UpsertAsync(updated, cancellationToken);

        logger.LogInformation("Installed mount {Mount} on ship {Ship} for {Cost} credits.", command.MountSymbol, command.ShipSymbol, result.Cost);
    }
}
