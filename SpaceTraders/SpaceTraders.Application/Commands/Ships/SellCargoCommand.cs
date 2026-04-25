using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public record SellCargoCommand(string ShipSymbol, string TradeSymbol, int Units);

public sealed class SellCargoHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    IMessageBus bus,
    ILogger<SellCargoHandler> logger)
{
    public async Task Handle(SellCargoCommand command, CancellationToken cancellationToken)
    {
        var result = await apiClient.SellCargoAsync(command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var cachedAgent = await db.Agents.FindAsync([result.Agent.Symbol], cancellationToken);
        if (cachedAgent is not null)
        {
            cachedAgent.Credits = result.Agent.Credits;
            cachedAgent.LastSyncedAt = now;
        }

        var cachedShip = await db.Ships.FindAsync([command.ShipSymbol], cancellationToken);
        if (cachedShip is not null)
        {
            cachedShip.CargoCurrent = result.Cargo.Units;
            cachedShip.CargoCapacity = result.Cargo.Capacity;
            cachedShip.LastSyncedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var soldEvent = new ShipCargoSoldEvent(
            command.ShipSymbol,
            new Domain.ValueObjects.TradeSymbol(command.TradeSymbol),
            command.Units,
            result.Transaction.TotalPrice,
            result.Agent.Credits);

        await bus.PublishAsync(soldEvent);

        logger.LogInformation("Ship {Symbol} sold {Units}x {Good} for {Revenue} credits.",
            command.ShipSymbol, command.Units, command.TradeSymbol, result.Transaction.TotalPrice);
    }
}
