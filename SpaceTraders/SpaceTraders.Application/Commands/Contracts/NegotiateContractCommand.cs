using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Commands.Contracts;

public sealed record NegotiateContractCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NegotiateContractCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class NegotiateContractHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IContractRepository contracts,
    ILogger<NegotiateContractHandler> logger)
{
    public async Task Handle(NegotiateContractCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (!string.Equals(ship?.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Skipping contract negotiation for ship {Symbol}: expected DOCKED but was {Status}.",
                command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return;
        }

        var result = await port.NegotiateContractAsync(command.ShipSymbol, cancellationToken);
        var dto = new ContractDto(
            result.Contract.Id,
            result.Contract.FactionSymbol,
            result.Contract.Type,
            result.Contract.IsAccepted,
            result.Contract.IsFulfilled,
            result.Contract.Expiration,
            result.Contract.DeadlineToAccept);
        await contracts.UpsertAsync(dto, cancellationToken);

        logger.LogInformation("Ship {Symbol} negotiated new contract {ContractId}.",
            command.ShipSymbol, result.Contract.Id);
    }
}
