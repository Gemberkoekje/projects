using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.EventHandlers;

public sealed class InitialAssignmentHandler(
    ITradeOpportunityRepository tradeOpportunities,
    ISettingsRepository settings,
    IMessageBus bus,
    ILogger<InitialAssignmentHandler> logger)
{
    public async Task Handle(NewShipPurchasedEvent @event, CancellationToken cancellationToken)
    {
        var minProfit = await settings.GetAsync<int>("Trade.MinProfitPerUnit", cancellationToken);
        var maxDistance = await settings.GetAsync<int>("Trade.MaxHaulDistance", cancellationToken);

        var route = await tradeOpportunities.GetBestRouteForCapacityAsync(
            cargoCapacity: int.MaxValue,
            minProfitPerUnit: minProfit,
            maxDistanceJumps: maxDistance,
            cancellationToken: cancellationToken);

        if (route is not null)
        {
            logger.LogInformation(
                "New ship {Symbol}: assigning Trade route {Good} {Buy} → {Sell} (profit/unit: {Profit}).",
                @event.ShipSymbol, route.TradeSymbol, route.BuyWaypoint, route.SellWaypoint, route.ProfitPerUnit);

            await bus.SendAsync(new AssignShipCommand(
                @event.ShipSymbol,
                "Trade",
                OriginWaypoint: route.BuyWaypoint,
                DestWaypoint: route.SellWaypoint,
                CargoSymbol: route.TradeSymbol));
        }
        else
        {
            logger.LogInformation("New ship {Symbol}: no trade route available; assigning Idle.", @event.ShipSymbol);
            await bus.SendAsync(new AssignShipCommand(@event.ShipSymbol, "Idle"));
        }
    }
}
