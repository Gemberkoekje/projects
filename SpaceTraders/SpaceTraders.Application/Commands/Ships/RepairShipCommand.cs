using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
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
    ILogger<RepairShipHandler> logger)
{
    public async Task Handle(RepairShipCommand command, CancellationToken cancellationToken)
    {
        var result = await port.RepairShipAsync(command.ShipSymbol, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);
        await ships.UpsertAsync(result.Ship, cancellationToken);

        logger.LogInformation("Repaired ship {Symbol} for {Cost} credits.", command.ShipSymbol, result.Cost);
    }
}
