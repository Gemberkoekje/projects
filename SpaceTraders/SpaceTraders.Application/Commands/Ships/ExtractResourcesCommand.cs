using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record ExtractResourcesCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ExtractResourcesCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class ExtractResourcesHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<ExtractResourcesHandler> logger)
{
    public async Task Handle(ExtractResourcesCommand command, CancellationToken cancellationToken)
    {
        var result = await port.ExtractResourcesAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);
        logger.LogInformation("Ship {Symbol} extracted {Units}x {Good}. Cooldown: {Cooldown}s.",
            command.ShipSymbol, result.YieldUnits, result.YieldSymbol, result.CooldownSeconds);
    }
}
