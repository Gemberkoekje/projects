using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
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
    public Task Handle(InstallMountCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(InstallMountCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.Docked)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(InstallMountCommand),
                "DOCKED_AT_SHIPYARD",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked at a shipyard waypoint before installing a mount.");

            logger.LogWarning("Skipping install mount for ship {Symbol}: expected DOCKED but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(ship!.WaypointSymbol))
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(InstallMountCommand),
                "DOCKED_AT_SHIPYARD",
                "DOCKED_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate shipyard.");

            logger.LogWarning("Skipping install mount for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                string.Empty);
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        if (waypoint?.HasShipyard != true)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(InstallMountCommand),
                "DOCKED_AT_SHIPYARD",
                "DOCKED_AT_NON_SHIPYARD_WAYPOINT",
                "Ship is not docked at a shipyard waypoint.");

            logger.LogWarning("Skipping install mount for ship {Symbol}: waypoint {Waypoint} is not a shipyard.", command.ShipSymbol, ship.WaypointSymbol);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol);
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

        await bus.PublishAsync(new MountInstalledEvent(
            command.ShipSymbol,
            command.MountSymbol,
            result.Cost,
            result.Agent.Credits,
            ship.WaypointSymbol ?? string.Empty));

        logger.LogInformation("Installed mount {Mount} on ship {Ship} for {Cost} credits.", command.MountSymbol, command.ShipSymbol, result.Cost);

        return new ShipCommandResult(
            command.ShipSymbol,
            currentStatus,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: result.Cargo.Units,
            CargoCapacity: result.Cargo.Capacity);
    }
}
