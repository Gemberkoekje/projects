using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships;

public record BuyCargoCommand(string ShipSymbol, string TradeSymbol, int Units);

public sealed class BuyCargoHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    ILogger<BuyCargoHandler> logger)
{
    public async Task Handle(BuyCargoCommand command, CancellationToken cancellationToken)
    {
        var result = await port.BuyCargoAsync(command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);

        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is not null)
            await agents.UpsertAsync(agent with { Credits = result.AgentCredits }, cancellationToken);

        logger.LogInformation("Ship {Symbol} bought {Units}x {Good} for {Cost} credits.",
            command.ShipSymbol, command.Units, command.TradeSymbol, result.Revenue);
    }
}
