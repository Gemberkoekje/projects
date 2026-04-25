using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.Application.Commands.Ships;

public record BuyCargoCommand(string ShipSymbol, string TradeSymbol, int Units);

public sealed class BuyCargoHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    ILogger<BuyCargoHandler> logger)
{
    public async Task Handle(BuyCargoCommand command, CancellationToken cancellationToken)
    {
        var result = await apiClient.BuyCargoAsync(command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);

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
        logger.LogInformation("Ship {Symbol} bought {Units}x {Good} for {Cost} credits.",
            command.ShipSymbol, command.Units, command.TradeSymbol, result.Transaction.TotalPrice);
    }
}
