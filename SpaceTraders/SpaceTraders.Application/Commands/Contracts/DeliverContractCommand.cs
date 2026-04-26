using Microsoft.Extensions.Logging;
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

        var result = await port.DeliverContractAsync(command.ContractId, command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);

        await contracts.UpdateStatusAsync(result.ContractId, result.IsAccepted, result.IsFulfilled, cancellationToken);

        if (result.ShipCargo is not null)
        {
            await ships.UpdateCargoAsync(command.ShipSymbol, result.ShipCargo, cancellationToken);
        }

        logger.LogInformation("Ship {Symbol} delivered {Units}x {Good} for contract {ContractId}.",
            command.ShipSymbol, command.Units, command.TradeSymbol, command.ContractId);
    }
}
