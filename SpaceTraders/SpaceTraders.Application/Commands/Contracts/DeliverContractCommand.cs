using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Contracts;

public sealed record DeliverContractCommand
{
    public required string ContractId { get; init; }

    public required string ShipSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    public required int Units { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public DeliverContractCommand(string ContractId, string ShipSymbol, string TradeSymbol, int Units)
    {
        this.ContractId = ContractId;
        this.ShipSymbol = ShipSymbol;
        this.TradeSymbol = TradeSymbol;
        this.Units = Units;
    }
}

public sealed class DeliverContractHandler(
    ISpaceTradersPort port,
    IContractRepository contracts,
    IShipRepository ships,
    ILogger<DeliverContractHandler> logger)
{
    public async Task Handle(DeliverContractCommand command, CancellationToken cancellationToken)
    {
        var result = await port.DeliverContractAsync(command.ContractId, command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);

        await contracts.UpdateStatusAsync(result.ContractId, result.IsAccepted, result.IsFulfilled, cancellationToken);

        if (result.ShipCargo is not null)
            await ships.UpdateCargoAsync(command.ShipSymbol, result.ShipCargo, cancellationToken);

        logger.LogInformation("Ship {Symbol} delivered {Units}x {Good} for contract {ContractId}.",
            command.ShipSymbol, command.Units, command.TradeSymbol, command.ContractId);
    }
}
