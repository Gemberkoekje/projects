using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;

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

    public async Task Handle(ShipBecameIdleEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipBecameIdleEvent),
            $"Ship {@event.ShipSymbol} became idle. Reason: {@event.Reason}.",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(ShipAssignedEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipAssignedEvent),
            $"Ship {@event.ShipSymbol} assigned to {@event.AssignmentType}.",
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

    public async Task Handle(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipDockedEvent),
            $"Ship {@event.ShipSymbol} docked at {@event.WaypointSymbol}.",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(ShipIdleDockedEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipIdleDockedEvent),
            $"Ship {@event.ShipSymbol} is idle and docked at {@event.WaypointSymbol}; awaiting role assignment.",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(ShipUndockedEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipUndockedEvent),
            $"Ship {@event.ShipSymbol} undocked and is in orbit at {@event.WaypointSymbol}.",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(ShipInTransitEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipInTransitEvent),
            $"Ship {@event.ShipSymbol} in transit to {@event.DestinationWaypointSymbol}, arriving at {@event.ArrivalTime:u}.",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(ShipStateMismatchEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipStateMismatchEvent),
            $"Ship {@event.ShipSymbol} state mismatch in {@event.CommandName}: expected {@event.RequiredState}, was {@event.ActualState}. {@event.Reason}",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipInOrbitEvent),
            $"Ship {@event.ShipSymbol} entered orbit at {@event.WaypointSymbol}.",
            cancellationToken: cancellationToken);
    }

    public async Task Handle(ShipAssignmentTypeSetEvent @event, CancellationToken cancellationToken)
    {
        await activityLog.AppendAsync(
            @event.ShipSymbol,
            nameof(ShipAssignmentTypeSetEvent),
            $"Ship {@event.ShipSymbol} assignment type set to {@event.AssignmentType}.",
            cancellationToken: cancellationToken);
    }
}
