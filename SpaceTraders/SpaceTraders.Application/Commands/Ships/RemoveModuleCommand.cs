using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record RemoveModuleCommand
{
    public required string ShipSymbol { get; init; }

    public required string ModuleSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RemoveModuleCommand(string ShipSymbol, string ModuleSymbol)
    {
        this.ShipSymbol = ShipSymbol;
        this.ModuleSymbol = ModuleSymbol;
    }
}

public sealed class RemoveModuleHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    ILogger<RemoveModuleHandler> logger)
{
    public async Task Handle(RemoveModuleCommand command, CancellationToken cancellationToken)
    {
        var result = await port.RemoveModuleAsync(command.ShipSymbol, command.ModuleSymbol, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (ship is not null)
        {
            var updated = ship with
            {
                ModulesJson = result.ModulesJson,
                CargoCurrent = result.Cargo.Units,
                CargoCapacity = result.Cargo.Capacity,
                CargoInventory = result.Cargo.Inventory,
            };
            await ships.UpsertAsync(updated, cancellationToken);
        }

        logger.LogInformation("Removed module {Module} from ship {Ship} for {Cost} credits.", command.ModuleSymbol, command.ShipSymbol, result.Cost);
    }
}
