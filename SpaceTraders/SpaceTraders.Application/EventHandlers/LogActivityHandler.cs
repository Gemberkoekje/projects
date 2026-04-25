using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

public sealed class LogActivityHandler(IActivityLogRepository activityLog)
{
    public async Task Handle(ShipCargoSoldEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipCargoSoldEvent),
            $"Sold {@event.Units}x {@event.Good.Value} for {@event.Revenue:N0} credits. Agent credits: {@event.NewAgentCredits:N0}.",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(ContractFulfilledEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            "AGENT",
            nameof(ContractFulfilledEvent),
            $"Contract {@event.ContractId} fulfilled. Payment: {@event.Payment:N0} credits.",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(NewShipPurchasedEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(NewShipPurchasedEvent),
            $"Purchased ship {@event.ShipSymbol} (type: {@event.Type}) for {@event.CostPaid:N0} credits.",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(ShipArrivedAtWaypointEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipArrivedAtWaypointEvent),
            $"Ship {@event.ShipSymbol} arrived at {@event.Waypoint.Value}.",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(ShipFuelLowEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipFuelLowEvent),
            $"Ship {@event.ShipSymbol} fuel low: {@event.CurrentFuel}/{@event.Capacity}.",
            cancellationToken: cancellationToken);
    }
}
