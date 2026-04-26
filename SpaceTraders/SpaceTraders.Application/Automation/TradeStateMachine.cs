using Stateless;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Stateless state machine for the Trade assignment
///
/// States (map 1:1 to persisted StepIndex):
///   NavigateToBuy   = 0
///   DockAtBuy       = 1
///   BuyCargo        = 2
///   NavigateToSell  = 3
///   DockAtSell      = 4
///   SellCargo       = 5
///   Completed       = 6
/// </summary>
internal sealed class TradeStateMachine
{
    internal enum TradeStep
    {
        None = -1,
        NavigateToBuy = 0,
        DockAtBuy = 1,
        BuyCargo = 2,
        NavigateToSell = 3,
        DockAtSell = 4,
        SellCargo = 5,
        Completed = 6,
    }

    private enum Trigger { Advance, Complete }

    private readonly StateMachine<TradeStep, Trigger> _machine;
    private readonly ShipModel _ship;
    private readonly ShipAssignmentDto _assignment;
    private readonly IShipAssignmentRepository _assignments;
    private readonly ITradeOpportunityRepository _tradeOpportunities;
    private readonly ISettingsRepository _settings;
    private readonly IMarketRepository _markets;
    private readonly IMessageBus _bus;
    private readonly ILogger _logger;

    public TradeStateMachine(
        ShipModel ship,
        ShipAssignmentDto assignment,
        IShipAssignmentRepository assignments,
        ITradeOpportunityRepository tradeOpportunities,
        ISettingsRepository settings,
        IMarketRepository markets,
        IMessageBus bus,
        ILogger logger)
    {
        _ship = ship;
        _assignment = assignment;
        _assignments = assignments;
        _tradeOpportunities = tradeOpportunities;
        _settings = settings;
        _markets = markets;
        _bus = bus;
        _logger = logger;

        var initialState = assignment.StepIndex >= 0 && assignment.StepIndex <= (int)TradeStep.Completed
            ? (TradeStep)assignment.StepIndex
            : TradeStep.Completed;

        _machine = new StateMachine<TradeStep, Trigger>(initialState);

        _machine.Configure(TradeStep.NavigateToBuy).Permit(Trigger.Advance, TradeStep.DockAtBuy);
        _machine.Configure(TradeStep.DockAtBuy).Permit(Trigger.Advance, TradeStep.BuyCargo);
        _machine.Configure(TradeStep.BuyCargo).Permit(Trigger.Advance, TradeStep.NavigateToSell);
        _machine.Configure(TradeStep.NavigateToSell).Permit(Trigger.Advance, TradeStep.DockAtSell);
        _machine.Configure(TradeStep.DockAtSell).Permit(Trigger.Advance, TradeStep.SellCargo);
        _machine.Configure(TradeStep.SellCargo).Permit(Trigger.Complete, TradeStep.Completed);
        _machine.Configure(TradeStep.Completed).OnEntry(() => { });
    }

    public async Task AdvanceAsync(CancellationToken cancellationToken)
    {
        switch (_machine.State)
        {
            case TradeStep.NavigateToBuy:
                if (_assignment.OriginWaypoint is null)
                {
                    _logger.LogWarning("Ship {Symbol} Trade assignment missing OriginWaypoint. Marking complete.", _ship.Symbol);
                    await MarkCompleteAsync(cancellationToken);
                    return;
                }
                if (!IsAtWaypoint(_assignment.OriginWaypoint))
                    await _bus.SendAsync(new NavigateShipCommand(_ship.Symbol, _assignment.OriginWaypoint));
                await AdvanceStepAsync(cancellationToken);
                break;

            case TradeStep.DockAtBuy:
                if (IsAtWaypoint(_assignment.OriginWaypoint) && !IsDocked())
                    await _bus.SendAsync(new DockShipCommand(_ship.Symbol));
                await AdvanceStepAsync(cancellationToken);
                break;

            case TradeStep.BuyCargo:
                if (_assignment.CargoSymbol is not null && IsDocked())
                {
                    var marketSystem = _ship.SystemSymbol ?? ExtractSystemSymbol(_assignment.OriginWaypoint);
                    if (!string.IsNullOrWhiteSpace(marketSystem) && _assignment.OriginWaypoint is not null)
                    {
                        await _bus.SendAsync(new RefreshMarketDataCommand(marketSystem, _assignment.OriginWaypoint, ForceRefresh: true));
                    }

                    if (!await IsTradeRouteStillViableAsync(cancellationToken))
                    {
                        _logger.LogInformation(
                            "Ship {Symbol} trade assignment no longer viable after market refresh. Completing assignment for replanning.",
                            _ship.Symbol);
                        await MarkCompleteAsync(cancellationToken);
                        return;
                    }

                    var units = _ship.CargoCapacity - _ship.CargoCurrent;
                    if (units > 0)
                    {
                        await _bus.SendAsync(new BuyCargoCommand(_ship.Symbol, _assignment.CargoSymbol, units));
                        await CapturePurchaseUnitPriceAsync(cancellationToken);
                    }
                }
                await AdvanceStepAsync(cancellationToken);
                break;

            case TradeStep.NavigateToSell:
                if (_assignment.DestWaypoint is null)
                {
                    _logger.LogWarning("Ship {Symbol} Trade assignment missing DestWaypoint. Marking complete.", _ship.Symbol);
                    await MarkCompleteAsync(cancellationToken);
                    return;
                }
                if (!IsAtWaypoint(_assignment.DestWaypoint))
                {
                    if (IsDocked())
                        await _bus.SendAsync(new OrbitShipCommand(_ship.Symbol));
                    else
                        await _bus.SendAsync(new NavigateShipCommand(_ship.Symbol, _assignment.DestWaypoint));
                }
                await AdvanceStepAsync(cancellationToken);
                break;

            case TradeStep.DockAtSell:
                if (IsAtWaypoint(_assignment.DestWaypoint) && !IsDocked())
                    await _bus.SendAsync(new DockShipCommand(_ship.Symbol));
                await AdvanceStepAsync(cancellationToken);
                break;

            case TradeStep.SellCargo:
                if (_assignment.CargoSymbol is not null && _ship.CargoCurrent > 0 && IsDocked())
                {
                    var marketSystem = _ship.SystemSymbol ?? ExtractSystemSymbol(_assignment.DestWaypoint);
                    if (!string.IsNullOrWhiteSpace(marketSystem) && _assignment.DestWaypoint is not null)
                    {
                        await _bus.SendAsync(new RefreshMarketDataCommand(marketSystem, _assignment.DestWaypoint, ForceRefresh: true));
                    }

                    var decision = await EvaluateSellDecisionAsync(cancellationToken);
                    if (decision.ShouldReroute && decision.BetterSellWaypoint is not null)
                    {
                        _logger.LogInformation(
                            "Ship {Symbol} deferring sell at {Waypoint}. Rerouting to {BetterWaypoint} for {CargoSymbol}.",
                            _ship.Symbol,
                            _assignment.DestWaypoint,
                            decision.BetterSellWaypoint,
                            _assignment.CargoSymbol);

                        await ReRouteToAlternativeSellWaypointAsync(decision.BetterSellWaypoint, cancellationToken);
                        return;
                    }

                    await _bus.SendAsync(new SellCargoCommand(_ship.Symbol, _assignment.CargoSymbol, _ship.CargoCurrent));
                    await MarkCompleteOnlyAsync(cancellationToken);
                }
                else
                {
                    await MarkCompleteAsync(cancellationToken);
                }
                _logger.LogInformation("Ship {Symbol} completed Trade assignment.", _ship.Symbol);
                break;

            default:
                _logger.LogWarning("Ship {Symbol} has unexpected Trade step {Step}. Marking complete.", _ship.Symbol, _machine.State);
                await MarkCompleteAsync(cancellationToken);
                break;
        }
    }

    private bool IsAtWaypoint(string? waypointSymbol)
        => waypointSymbol is not null &&
           _ship.WaypointSymbol?.Equals(waypointSymbol, StringComparison.OrdinalIgnoreCase) == true;

    private bool IsDocked()
        => _ship.Status?.Equals("DOCKED", StringComparison.OrdinalIgnoreCase) == true;

    private async Task AdvanceStepAsync(CancellationToken cancellationToken)
    {
        await _machine.FireAsync(Trigger.Advance);
        var updated = _assignment with { StepIndex = (int)_machine.State };
        await _assignments.UpsertAsync(updated, cancellationToken);
    }

    private async Task MarkCompleteAsync(CancellationToken cancellationToken)
    {
        await MarkCompleteAsync(AssignmentType.Trade, cancellationToken);
    }

    private async Task MarkCompleteOnlyAsync(CancellationToken cancellationToken)
    {
        var completed = _assignment with { CompletedAt = TimeProvider.System.GetUtcNow() };
        await _assignments.UpsertAsync(completed, cancellationToken);
    }

    private async Task MarkCompleteAsync(AssignmentType completedType, CancellationToken cancellationToken)
    {
        var completed = _assignment with { CompletedAt = TimeProvider.System.GetUtcNow() };
        await _assignments.UpsertAsync(completed, cancellationToken);
        await _bus.PublishAsync(new ShipAssignmentCompletedEvent(_ship.Symbol, completedType));
    }

    private async Task<bool> IsTradeRouteStillViableAsync(CancellationToken cancellationToken)
    {
        if (_assignment.CargoSymbol is null || _assignment.OriginWaypoint is null || _assignment.DestWaypoint is null)
        {
            return false;
        }

        var minProfit = await _settings.GetAsync<int>("Trade.MinProfitPerUnit", cancellationToken);
        var maxDistance = await _settings.GetAsync<int>("Trade.MaxHaulDistance", cancellationToken);
        var route = await _tradeOpportunities.GetBestRouteForCapacityAsync(
            cargoCapacity: _ship.CargoCapacity > 0 ? _ship.CargoCapacity : int.MaxValue,
            minProfitPerUnit: minProfit,
            maxDistanceJumps: maxDistance,
            cancellationToken: cancellationToken);

        return route is not null
            && route.TradeSymbol.Equals(_assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase)
            && route.BuyWaypoint.Equals(_assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase)
            && route.SellWaypoint.Equals(_assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase);
    }

    private async Task CapturePurchaseUnitPriceAsync(CancellationToken cancellationToken)
    {
        if (_assignment.CargoSymbol is null || _assignment.OriginWaypoint is null)
        {
            return;
        }

        var refreshed = await _markets.FindSnapshotByWaypointAsync(_assignment.OriginWaypoint, cancellationToken);
        var good = refreshed?.TradeGoods.FirstOrDefault(g => g.Symbol.Equals(_assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase));
        if (good is null || good.PurchasePrice <= 0)
        {
            return;
        }

        await _assignments.UpsertAsync(_assignment with { PurchaseUnitPrice = good.PurchasePrice }, cancellationToken);
    }

    private async Task<SellDecision> EvaluateSellDecisionAsync(CancellationToken cancellationToken)
    {
        if (_assignment.CargoSymbol is null || _assignment.DestWaypoint is null)
        {
            return SellDecision.SellNow();
        }

        if (_assignment.PurchaseUnitPrice <= 0)
        {
            return SellDecision.SellNow();
        }

        var currentMarket = await _markets.FindSnapshotByWaypointAsync(_assignment.DestWaypoint, cancellationToken);
        var currentGood = currentMarket?.TradeGoods.FirstOrDefault(g => g.Symbol.Equals(_assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase));
        if (currentGood is null)
        {
            return SellDecision.SellNow();
        }

        var currentSellPrice = currentGood.SellPrice;
        var lossPerUnit = _assignment.PurchaseUnitPrice - currentSellPrice;
        if (lossPerUnit <= 0)
        {
            return SellDecision.SellNow();
        }

        var maxAcceptedLoss = await _settings.GetAsync<int>("Automation.Trade.MaxLossPerUnitBeforeReroute", cancellationToken);
        if (lossPerUnit <= maxAcceptedLoss)
        {
            _logger.LogInformation(
                "Ship {Symbol} selling {CargoSymbol} at a controlled loss ({LossPerUnit}/unit <= threshold {Threshold}).",
                _ship.Symbol,
                _assignment.CargoSymbol,
                lossPerUnit,
                maxAcceptedLoss);
            return SellDecision.SellNow();
        }

        var minProfit = await _settings.GetAsync<int>("Trade.MinProfitPerUnit", cancellationToken);
        var maxDistance = await _settings.GetAsync<int>("Trade.MaxHaulDistance", cancellationToken);
        var bestRoute = await _tradeOpportunities.GetBestRouteForCapacityAsync(
            cargoCapacity: _ship.CargoCapacity > 0 ? _ship.CargoCapacity : int.MaxValue,
            minProfitPerUnit: minProfit,
            maxDistanceJumps: maxDistance,
            cancellationToken: cancellationToken);

        if (bestRoute is null || !bestRoute.TradeSymbol.Equals(_assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return SellDecision.SellNow();
        }

        if (bestRoute.SellWaypoint.Equals(_assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase))
        {
            return SellDecision.SellNow();
        }

        if (bestRoute.ProfitPerUnit <= 0)
        {
            return SellDecision.SellNow();
        }

        return SellDecision.Reroute(bestRoute.SellWaypoint);
    }

    private async Task ReRouteToAlternativeSellWaypointAsync(string newSellWaypoint, CancellationToken cancellationToken)
    {
        var rerouted = _assignment with
        {
            DestWaypoint = newSellWaypoint,
            StepIndex = (int)TradeStep.NavigateToSell,
        };

        await _assignments.UpsertAsync(rerouted, cancellationToken);
        await _bus.SendAsync(new OrbitShipCommand(_ship.Symbol));
    }

    private static string? ExtractSystemSymbol(string? waypointSymbol)
    {
        if (string.IsNullOrWhiteSpace(waypointSymbol))
        {
            return null;
        }

        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }

    private sealed record SellDecision
    {
        public required bool ShouldReroute { get; init; }

        public string BetterSellWaypoint { get; init; } = string.Empty;

        public static SellDecision SellNow() => new() { ShouldReroute = false };

        public static SellDecision Reroute(string betterSellWaypoint) => new()
        {
            ShouldReroute = true,
            BetterSellWaypoint = betterSellWaypoint,
        };
    }
}
