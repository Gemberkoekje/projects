using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record InstallModuleCommand
{
    public required string ShipSymbol { get; init; }

    public required string ModuleSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public InstallModuleCommand(string ShipSymbol, string ModuleSymbol)
    {
        this.ShipSymbol = ShipSymbol;
        this.ModuleSymbol = ModuleSymbol;
    }
}

public sealed class InstallModuleHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    IWaypointRepository waypoints,
    IMessageBus bus,
    ILogger<InstallModuleHandler> logger)
{
    public Task Handle(InstallModuleCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(InstallModuleCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.Docked)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(InstallModuleCommand),
                "DOCKED_AT_SHIPYARD",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked at a shipyard waypoint before installing a module.");

            logger.LogWarning("Skipping install module for ship {Symbol}: expected DOCKED but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
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
                nameof(InstallModuleCommand),
                "DOCKED_AT_SHIPYARD",
                "DOCKED_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate shipyard.");

            logger.LogWarning("Skipping install module for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
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
                nameof(InstallModuleCommand),
                "DOCKED_AT_SHIPYARD",
                "DOCKED_AT_NON_SHIPYARD_WAYPOINT",
                "Ship is not docked at a shipyard waypoint.");

            logger.LogWarning("Skipping install module for ship {Symbol}: waypoint {Waypoint} is not a shipyard.", command.ShipSymbol, ship.WaypointSymbol);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol);
        }

        var result = await port.InstallModuleAsync(command.ShipSymbol, command.ModuleSymbol, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);

        var updated = ship with
        {
            ModulesJson = result.ModulesJson,
            CargoCurrent = result.Cargo.Units,
            CargoCapacity = result.Cargo.Capacity,
            CargoInventory = result.Cargo.Inventory,
        };
        await ships.UpsertAsync(updated, cancellationToken);

        await bus.PublishAsync(new ModuleInstalledEvent(
            command.ShipSymbol,
            command.ModuleSymbol,
            result.Cost,
            result.Agent.Credits,
            ship.WaypointSymbol ?? string.Empty));

        logger.LogInformation("Installed module {Module} on ship {Ship} for {Cost} credits.", command.ModuleSymbol, command.ShipSymbol, result.Cost);

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
