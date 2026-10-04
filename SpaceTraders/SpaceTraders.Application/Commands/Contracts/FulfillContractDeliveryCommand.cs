using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
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
    ITradeContextReader tradeContexts,
    IDockSubCommand dock,
    IRefuelSubCommand refuel,
    IOrbitSubCommand orbit,
    IFlightModeSubCommand flightMode,
    INavigateSubCommand navigate,
    IMessageBus bus,
    IAgentRepository agents,
    IShipAssignmentRepository assignments,
    ITripBook trips,
    ILogger<FulfillContractDeliveryHandler> logger)
{
    private const string ContractAssignmentType = "Contract";

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
            // The contract's flight to the delivery: in CRUISE, through refuelling stops (B47).
            var map = (await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, cancellationToken)).Map;
            if (CommandFlight.DocksToRefuel(map, ship, command.DestinationWaypoint))
            {
                await dock.ExecuteAsync(ship.Symbol, cancellationToken);
                ship = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
            }

            if (ship.LocalStatus == ShipLocalStatus.Docked)
            {
                // A ship docked where fuel is sold fills its tank before it leaves, as on every flight.
                if (ship.FuelCurrent < ship.FuelCapacity && map.SellsFuel(ship.WaypointSymbol ?? string.Empty))
                {
                    await refuel.ExecuteAsync(ship.Symbol, fromCargo: false, cancellationToken);
                }

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

            await CommandFlight.TowardsAsync(map, ship, command.DestinationWaypoint, flightMode, navigate, cancellationToken);

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

        // At most what the contract still needs: a trip's last extraction can bring more aboard, the
        // surplus earns nothing, and the API may refuse the whole delivery for it.
        var units = UnitsToDeliver(
            cargoUnits,
            await contracts.FindAsync(command.ContractId, cancellationToken),
            command.TradeSymbol,
            command.DestinationWaypoint);

        if (units > 0)
        {
            var deliverResult = await port.DeliverContractAsync(
                command.ContractId,
                command.ShipSymbol,
                command.TradeSymbol,
                units,
                cancellationToken);

            await ships.UpdateCargoAsync(command.ShipSymbol, deliverResult.ShipCargo ?? new CargoModel(0, ship.CargoCapacity, []), cancellationToken);
            await contracts.UpsertAsync(MapToDto(deliverResult), cancellationToken);

            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} delivered {Units} {TradeSymbol} to {WaypointSymbol} for contract {ContractId}.",
                JournalEvents.ContractDelivered,
                command.ShipSymbol,
                units,
                command.TradeSymbol,
                command.DestinationWaypoint,
                command.ContractId);
        }

        // Several ships deliver to one contract (D23): only the first to find nothing pending fulfils it.
        var contract = await contracts.FindAsync(command.ContractId, cancellationToken);
        if (contract is not null && !contract.IsFulfilled && !HasPendingDeliverables(contract.DeliverablesJson))
        {
            var fulfilled = await port.FulfillContractAsync(command.ContractId, cancellationToken);
            await contracts.UpsertAsync(MapToDto(fulfilled), cancellationToken);

            // The payment: purchases are budgeted from the cached credits (B33); the ledger and the
            // metrics record it (B7).
            if (fulfilled.AgentCredits is { } credits)
            {
                await agents.SetCreditsAsync(bus, credits, cancellationToken);
            }

            await bus.PublishAsync(new ContractFulfilledEvent(command.ContractId, fulfilled.PaymentOnFulfilled));

            logger.LogInformation(
                "{EventKind:l}: contract {ContractId} fulfilled; it paid {Payment} credits.",
                JournalEvents.ContractFulfilled,
                command.ContractId,
                fulfilled.PaymentOnFulfilled);
        }

        // Only now: a ship whose fulfilment failed keeps its assignment, which sends it to make the
        // call again on the next tick.
        await EndTripAsync(command, cancellationToken);

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

    /// <summary>
    /// Closes the ship's contract assignment at its delivery (D26): the assignment lasts one round trip,
    /// and the plans assign the ship again on the next tick, in their order, so work that matters more
    /// comes first. A ship that can survey, for one, surveys once the survey plan is on (D20). The round
    /// trip is booked with the fuel it took (D46).
    /// </summary>
    private async Task EndTripAsync(FulfillContractDeliveryCommand command, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(command.ShipSymbol, cancellationToken);
        if (assignment is not { CompletedAt: null }
            || !assignment.AssignmentType.Equals(ContractAssignmentType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(assignment.ContractId, command.ContractId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await assignments.UpsertAsync(assignment with { CompletedAt = TimeProvider.System.GetUtcNow() }, cancellationToken);
        logger.LogDebug(
            "Contract trip over: ship {ShipSymbol} delivered for contract {ContractId}, and the plans assign it again on the next tick.",
            command.ShipSymbol,
            command.ContractId);
        await trips.BookContractTripAsync(command.ShipSymbol, assignment.AssignedAt, cancellationToken);
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

    private static int UnitsToDeliver(int cargoUnits, ContractDto? contract, string tradeSymbol, string destinationWaypoint)
    {
        var deliverable = contract is null
            ? null
            : DeserializeDeliverables(contract.DeliverablesJson).FirstOrDefault(d =>
                d.TradeSymbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase)
                && d.DestinationSymbol.Equals(destinationWaypoint, StringComparison.OrdinalIgnoreCase));

        // Terms not cached: deliver what is aboard, and the API decides.
        return deliverable is null
            ? cargoUnits
            : Math.Min(cargoUnits, Math.Max(0, deliverable.UnitsRequired - deliverable.UnitsFulfilled));
    }

    private static List<ContractDeliverableDto> DeserializeDeliverables(string? deliverablesJson)
    {
        if (string.IsNullOrWhiteSpace(deliverablesJson))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<ContractDeliverableDto>>(deliverablesJson) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
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
