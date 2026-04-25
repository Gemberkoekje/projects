using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public record SellCargoCommand(string ShipSymbol, string TradeSymbol, int Units);

public sealed class SellCargoHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    IMessageBus bus,
    ILogger<SellCargoHandler> logger)
{
    public async Task Handle(SellCargoCommand command, CancellationToken cancellationToken)
    {
        var result = await port.SellCargoAsync(command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);

        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is not null)
            await agents.UpsertAsync(agent with { Credits = result.AgentCredits }, cancellationToken);

        await bus.PublishAsync(new ShipCargoSoldEvent(
            command.ShipSymbol,
            new TradeSymbol(command.TradeSymbol),
            command.Units,
            result.Revenue,
            result.AgentCredits));

        logger.LogInformation("Ship {Symbol} sold {Units}x {Good} for {Revenue} credits.",
            command.ShipSymbol, command.Units, command.TradeSymbol, result.Revenue);
    }
}
