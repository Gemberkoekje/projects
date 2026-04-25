using Microsoft.Extensions.Logging;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using Wolverine;

namespace SpaceTraders.Application.Commands.Fleet;

public record PurchaseShipCommand(string ShipType, string ShipyardWaypoint);

public sealed class PurchaseShipHandler(
    ISpaceTradersApiClient apiClient,
    SpaceTradersDbContext db,
    IMessageBus bus,
    ILogger<PurchaseShipHandler> logger)
{
    public async Task Handle(PurchaseShipCommand command, CancellationToken cancellationToken)
    {
        var result = await apiClient.PurchaseShipAsync(command.ShipType, command.ShipyardWaypoint, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var cachedAgent = await db.Agents.FindAsync([result.Agent.Symbol], cancellationToken);
        if (cachedAgent is not null)
        {
            cachedAgent.Credits = result.Agent.Credits;
            cachedAgent.ShipCount = result.Agent.ShipCount;
            cachedAgent.LastSyncedAt = now;
        }

        db.Ships.Add(new CachedShip
        {
            Symbol = result.Ship.Symbol,
            SystemSymbol = result.Ship.Nav?.SystemSymbol,
            WaypointSymbol = result.Ship.Nav?.WaypointSymbol,
            Status = result.Ship.Nav?.Status,
            FlightMode = result.Ship.Nav?.FlightMode,
            FuelCurrent = result.Ship.Fuel?.Current ?? 0,
            FuelCapacity = result.Ship.Fuel?.Capacity ?? 0,
            LastSyncedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);

        if (Enum.TryParse<ShipType>(command.ShipType, true, out var shipTypeEnum))
        {
            await bus.PublishAsync(new NewShipPurchasedEvent(result.Ship.Symbol, shipTypeEnum, result.Transaction.Price));
        }

        logger.LogInformation("Purchased ship {Symbol} of type {Type} for {Cost} credits.",
            result.Ship.Symbol, command.ShipType, result.Transaction.Price);
    }
}
