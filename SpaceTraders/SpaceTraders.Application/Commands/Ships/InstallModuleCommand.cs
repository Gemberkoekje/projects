using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record InstallModuleCommand
{
    public required string ShipSymbol { get; init; }

    public required string ModuleSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public InstallModuleCommand(string ShipSymbol, string ModuleSymbol)
    {
        this.ShipSymbol = ShipSymbol;
        this.ModuleSymbol = ModuleSymbol;
    }
}

public sealed class InstallModuleHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    ILogger<InstallModuleHandler> logger)
{
    public async Task Handle(InstallModuleCommand command, CancellationToken cancellationToken)
    {
        var result = await port.InstallModuleAsync(command.ShipSymbol, command.ModuleSymbol, cancellationToken);

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

        logger.LogInformation("Installed module {Module} on ship {Ship} for {Cost} credits.", command.ModuleSymbol, command.ShipSymbol, result.Cost);
    }
}
