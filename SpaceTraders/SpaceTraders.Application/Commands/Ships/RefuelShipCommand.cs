using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record RefuelShipCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RefuelShipCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class RefuelShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    ILogger<RefuelShipHandler> logger)
{
    public async Task Handle(RefuelShipCommand command, CancellationToken cancellationToken)
    {
        var result = await port.RefuelShipAsync(command.ShipSymbol, cancellationToken);

        await ships.UpdateFuelAsync(command.ShipSymbol, result.Fuel, cancellationToken);

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is not null)
            await agents.UpsertAsync(agent with { Credits = result.AgentCredits }, cancellationToken);

        logger.LogInformation("Ship {Symbol} refuelled. Fuel: {Current}/{Capacity}. Cost: {Cost} credits.",
            command.ShipSymbol, result.Fuel.Current, result.Fuel.Capacity, result.Cost);
    }
}
