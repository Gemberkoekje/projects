using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Records credit-affecting domain events into the ledger for financial analytics
/// and notifies dashboard clients via SignalR.
/// </summary>
public sealed class LedgerEntryHandler(ILedgerRepository ledger, IDashboardNotifier notifier)
{
    public async Task Handle(ShipCargoSoldEvent @event, CancellationToken cancellationToken)
    {
        await ledger.AppendAsync(
            @event.ShipSymbol,
            LedgerCategory.TradeSell,
            @event.Revenue,
            goodSymbol: @event.Good.Value,
            units: @event.Units,
            cancellationToken: cancellationToken);
        notifier.Notify("ship", @event.ShipSymbol);
    }

    public async Task Handle(CargoPurchasedEvent @event, CancellationToken cancellationToken)
    {
        await ledger.AppendAsync(
            @event.ShipSymbol,
            LedgerCategory.TradeBuy,
            -@event.Cost,
            goodSymbol: @event.Good.Value,
            unitPrice: @event.Units > 0 ? (int)(@event.Cost / @event.Units) : null,
            units: @event.Units,
            waypointSymbol: @event.WaypointSymbol,
            cancellationToken: cancellationToken);
        notifier.Notify("ship", @event.ShipSymbol);
    }

    public async Task Handle(ShipRefueledEvent @event, CancellationToken cancellationToken)
    {
        await ledger.AppendAsync(
            @event.ShipSymbol,
            LedgerCategory.FuelPurchase,
            -@event.Cost,
            waypointSymbol: @event.WaypointSymbol,
            cancellationToken: cancellationToken);
        notifier.Notify("ship", @event.ShipSymbol);
    }

    public async Task Handle(ShipRepairedEvent @event, CancellationToken cancellationToken)
    {
        await ledger.AppendAsync(
            @event.ShipSymbol,
            LedgerCategory.Repair,
            -@event.Cost,
            waypointSymbol: @event.WaypointSymbol,
            cancellationToken: cancellationToken);
        notifier.Notify("ship", @event.ShipSymbol);
    }

    public async Task Handle(MountInstalledEvent @event, CancellationToken cancellationToken)
    {
        await ledger.AppendAsync(
            @event.ShipSymbol,
            LedgerCategory.MountPurchase,
            -@event.Cost,
            goodSymbol: @event.MountSymbol,
            waypointSymbol: @event.WaypointSymbol,
            cancellationToken: cancellationToken);
        notifier.Notify("ship", @event.ShipSymbol);
    }

    public async Task Handle(ModuleInstalledEvent @event, CancellationToken cancellationToken)
    {
        await ledger.AppendAsync(
            @event.ShipSymbol,
            LedgerCategory.ModulePurchase,
            -@event.Cost,
            goodSymbol: @event.ModuleSymbol,
            waypointSymbol: @event.WaypointSymbol,
            cancellationToken: cancellationToken);
        notifier.Notify("ship", @event.ShipSymbol);
    }

    public async Task Handle(NewShipPurchasedEvent @event, CancellationToken cancellationToken)
    {
        await ledger.AppendAsync(
            @event.ShipSymbol,
            LedgerCategory.ShipPurchase,
            -@event.CostPaid,
            goodSymbol: @event.Type.ToString(),
            cancellationToken: cancellationToken);
        notifier.Notify("ship", @event.ShipSymbol);
    }

    public async Task Handle(ContractFulfilledEvent @event, CancellationToken cancellationToken)
    {
        await ledger.AppendAsync(
            "AGENT",
            LedgerCategory.ContractPayout,
            @event.Payment,
            sourceEventId: @event.ContractId,
            cancellationToken: cancellationToken);
        notifier.Notify("contract", @event.ContractId);
    }
}
