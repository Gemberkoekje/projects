using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record SupplyConstructionCommand
{
    public required string ShipSymbol { get; init; }

    public required string SystemSymbol { get; init; }

    public required string WaypointSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    public required int Units { get; init; }

    public required Guid CorrelationId { get; init; }

    public required Guid CausationId { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SupplyConstructionCommand(
        string ShipSymbol,
        string SystemSymbol,
        string WaypointSymbol,
        string TradeSymbol,
        int Units,
        Guid CorrelationId,
        Guid CausationId)
    {
        this.ShipSymbol = ShipSymbol;
        this.SystemSymbol = SystemSymbol;
        this.WaypointSymbol = WaypointSymbol;
        this.TradeSymbol = TradeSymbol;
        this.Units = Units;
        this.CorrelationId = CorrelationId;
        this.CausationId = CausationId;
    }
}

public sealed class SupplyConstructionHandler(
    ISpaceTradersPort port,
    IConstructionRepository constructions,
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    IMessageBus bus,
    ILogger<SupplyConstructionHandler> logger)
{
    public Task Handle(SupplyConstructionCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(SupplyConstructionCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(SupplyConstructionCommand),
                "IN_ORBIT_AT_CONSTRUCTION_SITE",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit at the construction site before supplying.");

            logger.LogWarning(
                "Skipping construction supply for ship {Symbol}: expected IN_ORBIT but was {Status}.",
                command.ShipSymbol,
                ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        var cargoUnits = ship!.CargoInventory?
            .FirstOrDefault(i => i.Symbol.Equals(command.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        if (cargoUnits <= 0)
        {
            logger.LogInformation(
                "Skipping construction supply for ship {Symbol}: no {TradeSymbol} cargo available.",
                command.ShipSymbol,
                command.TradeSymbol);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty);
        }

        var unitsToSupply = Math.Min(cargoUnits, command.Units);

        logger.LogInformation(
            "Supplying {Units}x {TradeSymbol} from ship {Ship} to construction site {Waypoint}.",
            unitsToSupply,
            command.TradeSymbol,
            command.ShipSymbol,
            command.WaypointSymbol);

        var result = await port.SupplyConstructionAsync(
            command.SystemSymbol,
            command.WaypointSymbol,
            command.ShipSymbol,
            command.TradeSymbol,
            unitsToSupply,
            cancellationToken);

        await constructions.UpsertAsync(result.Construction, command.SystemSymbol, cancellationToken);

        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);

        // Mark the assignment so the docked handler knows the supply run is done
        var assignment = await assignments.FindAsync(command.ShipSymbol, cancellationToken);
        if (assignment is not null)
        {
            await assignments.UpsertAsync(new SpaceTraders.Application.DTOs.ShipAssignmentDto(
                assignment.ShipSymbol,
                assignment.AssignmentType,
                assignment.OriginWaypoint,
                assignment.DestWaypoint,
                assignment.CargoSymbol,
                assignment.ContractId,
                assignment.StepIndex,
                assignment.AssignedAt,
                assignment.CompletedAt,
                assignment.PurchaseUnitPrice,
                assignment.RequiredUnits,
                SupplyCompleted: true), cancellationToken);
        }

        var now = TimeProvider.System.GetUtcNow();
        await bus.PublishAsync(new ConstructionSuppliedEvent(
            command.ShipSymbol,
            command.SystemSymbol,
            command.WaypointSymbol,
            command.TradeSymbol,
            unitsToSupply,
            result.Construction.IsComplete,
            command.CorrelationId,
            command.CausationId,
            now));

        logger.LogInformation(
            "Construction site {Waypoint} supplied with {Units}x {TradeSymbol}. Complete: {IsComplete}.",
            command.WaypointSymbol,
            unitsToSupply,
            command.TradeSymbol,
            result.Construction.IsComplete);

        return new ShipCommandResult(
            command.ShipSymbol,
            currentStatus,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol ?? string.Empty,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: result.Cargo.Units,
            CargoCapacity: result.Cargo.Capacity);
    }
}
