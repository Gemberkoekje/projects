using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Handles <see cref="ShipFuelLowEvent"/> by dispatching a <see cref="RefuelShipCommand"/>.
/// Only triggers for ships that are docked, as refuelling requires the ship to be at a station.
/// </summary>
public sealed class ShipFuelLowHandler(
    IMessageBus bus,
    ILogger<ShipFuelLowHandler> logger)
{
    public async Task Handle(ShipFuelLowEvent @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Ship {ShipSymbol} fuel low ({Current}/{Capacity}); dispatching refuel command.",
            @event.ShipSymbol,
            @event.CurrentFuel,
            @event.Capacity);

        await bus.SendAsync(new RefuelShipCommand(@event.ShipSymbol));
    }
}
