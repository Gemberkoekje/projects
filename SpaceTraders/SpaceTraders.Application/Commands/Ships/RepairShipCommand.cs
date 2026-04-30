using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record RepairShipCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RepairShipCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class RepairShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    IMessageBus bus,
    ILogger<RepairShipHandler> logger)
{
    public Task Handle(RepairShipCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(RepairShipCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.Docked)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(RepairShipCommand),
                "DOCKED_AT_SHIPYARD",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked at a shipyard before repairing.");

            logger.LogWarning("Skipping repair for ship {Symbol}: expected DOCKED but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        var result = await port.RepairShipAsync(command.ShipSymbol, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);
        await ships.UpsertAsync(result.Ship, cancellationToken);

        logger.LogInformation("Repaired ship {Symbol} for {Cost} credits.", command.ShipSymbol, result.Cost);

        return new ShipCommandResult(
            command.ShipSymbol,
            ShipLocalStatusMapper.FromApiStatus(result.Ship.Status),
            result.Ship.SystemSymbol ?? ship!.SystemSymbol ?? string.Empty,
            result.Ship.WaypointSymbol ?? ship!.WaypointSymbol ?? string.Empty,
            FuelCurrent: result.Ship.FuelCurrent,
            FuelCapacity: result.Ship.FuelCapacity,
            CargoCurrent: result.Ship.CargoCurrent,
            CargoCapacity: result.Ship.CargoCapacity);
    }
}
