using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record RemoveMountCommand
{
    public required string ShipSymbol { get; init; }

    public required string MountSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RemoveMountCommand(string ShipSymbol, string MountSymbol)
    {
        this.ShipSymbol = ShipSymbol;
        this.MountSymbol = MountSymbol;
    }
}

public sealed class RemoveMountHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    ILogger<RemoveMountHandler> logger)
{
    public async Task Handle(RemoveMountCommand command, CancellationToken cancellationToken)
    {
        var result = await port.RemoveMountAsync(command.ShipSymbol, command.MountSymbol, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (ship is not null)
        {
            var updated = ship with
            {
                MountSymbols = result.MountSymbols,
                CargoCurrent = result.Cargo.Units,
                CargoCapacity = result.Cargo.Capacity,
                CargoInventory = result.Cargo.Inventory,
            };
            await ships.UpsertAsync(updated, cancellationToken);
        }

        logger.LogInformation("Removed mount {Mount} from ship {Ship} for {Cost} credits.", command.MountSymbol, command.ShipSymbol, result.Cost);
    }
}
