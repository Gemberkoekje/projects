using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Contracts;

public sealed record DeliverContractCommand
{
    public required string ContractId { get; init; }

    public required string ShipSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    public required int Units { get; init; }

    public string? DestinationWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public DeliverContractCommand(string ContractId, string ShipSymbol, string TradeSymbol, int Units, string? DestinationWaypoint = null)
    {
        this.ContractId = ContractId;
        this.ShipSymbol = ShipSymbol;
        this.TradeSymbol = TradeSymbol;
        this.Units = Units;
        this.DestinationWaypoint = DestinationWaypoint;
    }
}

public sealed class DeliverContractHandler(
    ISpaceTradersPort port,
    IContractRepository contracts,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<DeliverContractHandler> logger)
{
    public async Task Handle(DeliverContractCommand command, CancellationToken cancellationToken)
    {
        var contract = await contracts.FindAsync(command.ContractId, cancellationToken);
        if (contract is null)
        {
            logger.LogWarning("Skipping contract delivery for {ContractId}: contract not found in cache.", command.ContractId);
            return;
        }

        if (!contract.IsAccepted || contract.IsFulfilled)
        {
            logger.LogInformation("Skipping contract delivery for {ContractId}: accepted={Accepted}, fulfilled={Fulfilled}.", command.ContractId, contract.IsAccepted, contract.IsFulfilled);
            return;
        }

        var pendingUnits = ResolvePendingUnits(contract.DeliverablesJson, command.TradeSymbol, command.DestinationWaypoint);
        if (pendingUnits <= 0)
        {
            logger.LogInformation("Skipping contract delivery for {ContractId}: deliverable already fulfilled for {TradeSymbol}.", command.ContractId, command.TradeSymbol);
            return;
        }

        var unitsToDeliver = Math.Min(command.Units, pendingUnits);
        if (unitsToDeliver <= 0)
        {
            logger.LogInformation("Skipping contract delivery for {ContractId}: computed deliver units is 0.", command.ContractId);
            return;
        }

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (!string.Equals(ship?.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(DeliverContractCommand),
                "DOCKED_AT_CONTRACT_DESTINATION",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked before delivering contract goods.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping contract delivery for ship {Symbol}: expected DOCKED but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return;
        }

        if (!string.IsNullOrWhiteSpace(command.DestinationWaypoint)
            && !string.Equals(ship?.WaypointSymbol, command.DestinationWaypoint, StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(DeliverContractCommand),
                $"DOCKED_AT_{command.DestinationWaypoint}",
                $"DOCKED_AT_{ship?.WaypointSymbol ?? "UNKNOWN"}",
                "Ship is not docked at the contract destination waypoint.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping contract delivery for ship {Symbol}: expected waypoint {Expected} but was {Actual}.", command.ShipSymbol, command.DestinationWaypoint, ship?.WaypointSymbol ?? "UNKNOWN");
            return;
        }

        var result = await port.DeliverContractAsync(command.ContractId, command.ShipSymbol, command.TradeSymbol, unitsToDeliver, cancellationToken);

        await contracts.UpsertAsync(new ContractDto(
            result.ContractId,
            result.FactionSymbol,
            result.ContractType,
            result.IsAccepted,
            result.IsFulfilled,
            result.Expiration,
            result.DeadlineToAccept,
            result.TermsDeadline,
            JsonSerializer.Serialize(result.Deliverables.Select(d =>
                new ContractDeliverableDto(d.TradeSymbol, d.DestinationSymbol, d.UnitsRequired, d.UnitsFulfilled)).ToList())), cancellationToken);

        if (result.ShipCargo is not null)
        {
            await ships.UpdateCargoAsync(command.ShipSymbol, result.ShipCargo, cancellationToken);
        }

        await bus.PublishAsync(new SpaceTraders.Domain.Events.ContractDeliveryRecordedEvent(
            command.ContractId,
            command.ShipSymbol,
            command.TradeSymbol,
            unitsToDeliver,
            command.DestinationWaypoint ?? ship?.WaypointSymbol ?? string.Empty));

        logger.LogInformation("Ship {Symbol} delivered {Units}x {Good} for contract {ContractId}.",
            command.ShipSymbol, unitsToDeliver, command.TradeSymbol, command.ContractId);
    }

    private static int ResolvePendingUnits(string? deliverablesJson, string tradeSymbol, string? destinationWaypoint)
    {
        if (string.IsNullOrWhiteSpace(deliverablesJson))
        {
            return int.MaxValue;
        }

        try
        {
            var deliverables = JsonSerializer.Deserialize<List<ContractDeliverableDto>>(deliverablesJson) ?? [];
            var deliverable = deliverables.FirstOrDefault(d =>
                d.TradeSymbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(destinationWaypoint) || d.DestinationSymbol.Equals(destinationWaypoint, StringComparison.OrdinalIgnoreCase)));

            if (deliverable is null)
            {
                return int.MaxValue;
            }

            return Math.Max(0, deliverable.UnitsRequired - deliverable.UnitsFulfilled);
        }
        catch (JsonException)
        {
            return int.MaxValue;
        }
    }
}
