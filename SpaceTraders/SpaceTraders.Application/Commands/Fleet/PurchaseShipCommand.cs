using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Fleet;

public record PurchaseShipCommand(string ShipType, string ShipyardWaypoint);

public sealed class PurchaseShipHandler(
    ISpaceTradersPort port,
    IAgentRepository agents,
    IShipRepository ships,
    IMessageBus bus,
    ILogger<PurchaseShipHandler> logger)
{
    public async Task Handle(PurchaseShipCommand command, CancellationToken cancellationToken)
    {
        var result = await port.PurchaseShipAsync(command.ShipType, command.ShipyardWaypoint, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);

        var newShip = new ShipModel(
            result.ShipSymbol,
            result.ShipNav.SystemSymbol,
            result.ShipNav.WaypointSymbol,
            result.ShipNav.Status,
            result.ShipNav.FlightMode,
            result.ShipFuel.Current,
            result.ShipFuel.Capacity,
            result.ShipNav.ArrivesAt,
            result.ShipNav.DestWaypointSymbol);

        await ships.UpsertAsync(newShip, cancellationToken);

        if (Enum.TryParse<ShipType>(command.ShipType, true, out var shipTypeEnum))
        {
            await bus.PublishAsync(new NewShipPurchasedEvent(result.ShipSymbol, shipTypeEnum, result.Cost));
        }

        logger.LogInformation("Purchased ship {Symbol} of type {Type} for {Cost} credits.",
            result.ShipSymbol, command.ShipType, result.Cost);
    }
}
