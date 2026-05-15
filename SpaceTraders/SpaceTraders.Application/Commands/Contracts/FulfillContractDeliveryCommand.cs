using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Contracts;

public sealed record FulfillContractDeliveryCommand
{
    public required string ShipSymbol { get; init; }

    public required string ContractId { get; init; }

    public required string TradeSymbol { get; init; }

    public required string DestinationWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public FulfillContractDeliveryCommand(string ShipSymbol, string ContractId, string TradeSymbol, string DestinationWaypoint)
    {
        this.ShipSymbol = ShipSymbol;
        this.ContractId = ContractId;
        this.TradeSymbol = TradeSymbol;
        this.DestinationWaypoint = DestinationWaypoint;
    }
}

public sealed class FulfillContractDeliveryHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IContractRepository contracts,
    IDockSubCommand dock,
    IOrbitSubCommand orbit,
    INavigateSubCommand navigate,
    IMessageBus bus,
    ILogger<FulfillContractDeliveryHandler> logger)
{
    public Task Handle(FulfillContractDeliveryCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(FulfillContractDeliveryCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ShipCommandResult.Rejected(command.ShipSymbol, ShipLocalStatus.None, string.Empty, string.Empty);
        }

        ship = await ApplyArrivalDeadReckoningIfDueAsync(ship, cancellationToken);

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return ShipCommandResult.Rejected(
                ship.Symbol,
                ship.LocalStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty);
        }

        var atDestination = string.Equals(ship.WaypointSymbol, command.DestinationWaypoint, StringComparison.OrdinalIgnoreCase);

        if (!atDestination)
        {
            if (ship.LocalStatus == ShipLocalStatus.Docked)
            {
                await orbit.ExecuteAsync(ship.Symbol, cancellationToken);
                ship = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
            }

            if (ship.LocalStatus != ShipLocalStatus.InOrbit)
            {
                await bus.PublishMismatchAndTickAsync(
                    command.ShipSymbol,
                    nameof(FulfillContractDeliveryCommand),
                    "IN_ORBIT",
                    ship.Status ?? "UNKNOWN",
                    "Ship must be in orbit to navigate to contract destination.");

                return ShipCommandResult.Rejected(
                    ship.Symbol,
                    ship.LocalStatus,
                    ship.SystemSymbol ?? string.Empty,
                    ship.WaypointSymbol ?? string.Empty);
            }

            await navigate.ExecuteAsync(ship.Symbol, command.DestinationWaypoint, Guid.Empty, cancellationToken);

            return new ShipCommandResult(
                ship.Symbol,
                ShipLocalStatus.InTransit,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty,
                Accepted: true);
        }

        if (ship.LocalStatus == ShipLocalStatus.InOrbit)
        {
            await dock.ExecuteAsync(ship.Symbol, cancellationToken);
            ship = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
        }

        if (ship.LocalStatus != ShipLocalStatus.Docked)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(FulfillContractDeliveryCommand),
                "DOCKED",
                ship.Status ?? "UNKNOWN",
                "Ship must be docked at destination before contract delivery.");

            return ShipCommandResult.Rejected(
                ship.Symbol,
                ship.LocalStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty);
        }

        var cargoUnits = ship.CargoInventory?
            .FirstOrDefault(i => i.Symbol.Equals(command.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        if (cargoUnits > 0)
        {
            var deliverResult = await port.DeliverContractAsync(
                command.ContractId,
                command.ShipSymbol,
                command.TradeSymbol,
                cargoUnits,
                cancellationToken);

            await ships.UpdateCargoAsync(command.ShipSymbol, deliverResult.ShipCargo ?? new CargoModel(0, ship.CargoCapacity, []), cancellationToken);
            await contracts.UpsertAsync(MapToDto(deliverResult), cancellationToken);

            logger.LogInformation(
                "FulfillContractDelivery: ship {ShipSymbol} delivered {Units} {TradeSymbol} for contract {ContractId}.",
                command.ShipSymbol,
                cargoUnits,
                command.TradeSymbol,
                command.ContractId);
        }

        var contract = await contracts.FindAsync(command.ContractId, cancellationToken);
        if (contract is not null && !HasPendingDeliverables(contract.DeliverablesJson))
        {
            var fulfilled = await port.FulfillContractAsync(command.ContractId, cancellationToken);
            await contracts.UpsertAsync(MapToDto(fulfilled), cancellationToken);

            logger.LogInformation(
                "FulfillContractDelivery: contract {ContractId} fulfilled.",
                command.ContractId);
        }

        var refreshed = await ships.FindAsync(command.ShipSymbol, cancellationToken) ?? ship;

        return new ShipCommandResult(
            refreshed.Symbol,
            refreshed.LocalStatus,
            refreshed.SystemSymbol ?? string.Empty,
            refreshed.WaypointSymbol ?? string.Empty,
            FuelCurrent: refreshed.FuelCurrent,
            FuelCapacity: refreshed.FuelCapacity,
            CargoCurrent: refreshed.CargoCurrent,
            CargoCapacity: refreshed.CargoCapacity,
            Accepted: true);
    }

    private async Task<ShipModel> ApplyArrivalDeadReckoningIfDueAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (ship.LocalStatus != ShipLocalStatus.InTransit
            || !ship.ArrivesAt.HasValue
            || ship.ArrivesAt.Value > TimeProvider.System.GetUtcNow())
        {
            return ship;
        }

        var waypoint = ship.DestWaypointSymbol ?? ship.WaypointSymbol ?? string.Empty;
        var nav = new NavModel(
            Status: "IN_ORBIT",
            SystemSymbol: ship.SystemSymbol ?? string.Empty,
            WaypointSymbol: waypoint,
            FlightMode: ship.FlightMode ?? "CRUISE",
            DestWaypointSymbol: null,
            ArrivesAt: null);

        await ships.UpdateNavAsync(ship.Symbol, nav, null, cancellationToken);
        return await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
    }

    private static bool HasPendingDeliverables(string? deliverablesJson)
    {
        if (string.IsNullOrWhiteSpace(deliverablesJson))
        {
            return true;
        }

        try
        {
            var deliverables = System.Text.Json.JsonSerializer.Deserialize<List<ContractDeliverableDto>>(deliverablesJson) ?? [];
            return deliverables.Any(d => d.UnitsRequired > d.UnitsFulfilled);
        }
        catch (System.Text.Json.JsonException)
        {
            return true;
        }
    }

    private static ContractDto MapToDto(ContractActionResult result)
    {
        return new ContractDto(
            result.ContractId,
            result.FactionSymbol,
            result.ContractType,
            result.IsAccepted,
            result.IsFulfilled,
            result.Expiration,
            result.DeadlineToAccept,
            result.TermsDeadline,
            System.Text.Json.JsonSerializer.Serialize(result.Deliverables.Select(d =>
                new ContractDeliverableDto(d.TradeSymbol, d.DestinationSymbol, d.UnitsRequired, d.UnitsFulfilled)).ToList()));
    }
}
