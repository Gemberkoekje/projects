using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record GetShipCargoCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GetShipCargoCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class GetShipCargoHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<GetShipCargoHandler> logger)
{
    public async Task Handle(GetShipCargoCommand command, CancellationToken cancellationToken)
    {
        var cargo = await port.GetShipCargoAsync(command.ShipSymbol, cancellationToken);
        await ships.UpdateCargoAsync(command.ShipSymbol, cargo, cancellationToken);
        logger.LogInformation("Synced cargo for ship {Symbol}: {Units}/{Capacity}.",
            command.ShipSymbol, cargo.Units, cargo.Capacity);
    }
}
