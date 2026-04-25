using Microsoft.Extensions.Logging;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.Application.Commands.Ships;

public record RefuelShipCommand(string ShipSymbol);

public sealed class RefuelShipHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    ILogger<RefuelShipHandler> logger)
{
    public async Task Handle(RefuelShipCommand command, CancellationToken cancellationToken)
    {
        var result = await apiClient.RefuelShipAsync(command.ShipSymbol, cancellationToken);

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
            cachedShip.FuelCurrent = result.Fuel.Current;
            cachedShip.FuelCapacity = result.Fuel.Capacity;
            cachedShip.LastSyncedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Ship {Symbol} refuelled. Fuel: {Current}/{Capacity}. Cost: {Cost} credits.",
            command.ShipSymbol, result.Fuel.Current, result.Fuel.Capacity, result.Transaction.TotalPrice);
    }
}
