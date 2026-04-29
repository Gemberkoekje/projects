using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record ScrapShipCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ScrapShipCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class ScrapShipHandler(
    ISpaceTradersPort port,
    IAgentRepository agents,
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    ILogger<ScrapShipHandler> logger)
{
    public async Task Handle(ScrapShipCommand command, CancellationToken cancellationToken)
    {
        var result = await port.ScrapShipAsync(command.ShipSymbol, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);
        await ships.RemoveAsync(command.ShipSymbol, cancellationToken);

        await assignments.UpsertAsync(new SpaceTraders.Application.DTOs.ShipAssignmentDto(
            command.ShipSymbol,
            "Scrapped",
            null,
            null,
            null,
            null,
            0,
            TimeProvider.System.GetUtcNow(),
            TimeProvider.System.GetUtcNow()),
            cancellationToken);

        logger.LogInformation("Scrapped ship {Symbol} for {Value} credits.", command.ShipSymbol, result.Value);
    }
}
