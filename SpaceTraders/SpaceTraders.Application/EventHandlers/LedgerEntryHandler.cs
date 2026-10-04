using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Records credit-affecting domain events into the ledger for financial analytics, counts them in
/// the credits earned and spent metrics, and notifies dashboard clients via SignalR. A sale or a
/// purchase of cargo also counts its units, by market and good, whichever ship made it. A contract's
/// deposit and payout also count as its profit (D46); every other trip is booked when it ends
/// (<see cref="ITripBook"/>).
/// </summary>
public sealed class LedgerEntryHandler(ILedgerRepository ledger, IAutomationMetrics metrics, IDashboardNotifier notifier)
{
    public async Task Handle(ShipCargoSoldEvent @event, CancellationToken cancellationToken)
    {
        await ledger.AppendAsync(
            @event.ShipSymbol,
            LedgerCategory.TradeSell,
            @event.Revenue,
            goodSymbol: @event.Good.Value,
            unitPrice: @event.Units > 0 ? (int)(@event.Revenue / @event.Units) : null,
            units: @event.Units,
            waypointSymbol: @event.WaypointSymbol,
            cancellationToken: cancellationToken);
        CountCredits(LedgerCategory.TradeSell, @event.Revenue);
        metrics.GoodsSold(@event.WaypointSymbol, @event.Good.Value, @event.Units);
        notifier.Notify("ship", @event.ShipSymbol);
    }

    public async Task Handle(CargoPurchasedEvent @event, CancellationToken cancellationToken)
    {
        // Materials for a construction site are spent for good (slice 6.6): booked apart from trading's purchases.
        var category = @event.ForConstruction ? LedgerCategory.ConstructionBuy : LedgerCategory.TradeBuy;
        await ledger.AppendAsync(
            @event.ShipSymbol,
            category,
            -@event.Cost,
            goodSymbol: @event.Good.Value,
            unitPrice: @event.Units > 0 ? (int)(@event.Cost / @event.Units) : null,
            units: @event.Units,
            waypointSymbol: @event.WaypointSymbol,
            cancellationToken: cancellationToken);
        CountCredits(category, -@event.Cost);
        metrics.GoodsBought(@event.WaypointSymbol, @event.Good.Value, @event.Units);
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
        CountCredits(LedgerCategory.FuelPurchase, -@event.Cost);
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
        CountCredits(LedgerCategory.Repair, -@event.Cost);
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
        CountCredits(LedgerCategory.MountPurchase, -@event.Cost);
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
        CountCredits(LedgerCategory.ModulePurchase, -@event.Cost);
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
        CountCredits(LedgerCategory.ShipPurchase, -@event.CostPaid);
        notifier.Notify("ship", @event.ShipSymbol);
    }

    public async Task Handle(ContractAcceptedEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Payment == 0)
        {
            return;
        }

        await ledger.AppendAsync(
            "AGENT",
            LedgerCategory.ContractDeposit,
            @event.Payment,
            sourceEventId: @event.ContractId,
            cancellationToken: cancellationToken);
        CountCredits(LedgerCategory.ContractDeposit, @event.Payment);
        metrics.TripProfit(TripBook.Contract, @event.Payment);
        notifier.Notify("contract", @event.ContractId);
    }

    public async Task Handle(ContractFulfilledEvent @event, CancellationToken cancellationToken)
    {
        await ledger.AppendAsync(
            "AGENT",
            LedgerCategory.ContractPayout,
            @event.Payment,
            sourceEventId: @event.ContractId,
            cancellationToken: cancellationToken);
        CountCredits(LedgerCategory.ContractPayout, @event.Payment);
        metrics.TripProfit(TripBook.Contract, @event.Payment);
        notifier.Notify("contract", @event.ContractId);
    }

    private void CountCredits(LedgerCategory category, long amount)
    {
        if (amount > 0)
        {
            metrics.CreditsEarned(category.ToString(), amount);
        }
        else if (amount < 0)
        {
            metrics.CreditsSpent(category.ToString(), -amount);
        }
    }
}
