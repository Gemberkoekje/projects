using Stateless;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Stateless state machine for the Trade assignment.
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
        Completed = 6
    }

    private enum Trigger { Advance, Complete }

    private readonly StateMachine<TradeStep, Trigger> _machine;
    private readonly ShipModel _ship;
    private readonly ShipAssignmentDto _assignment;
    private readonly IShipAssignmentRepository _assignments;
    private readonly IMessageBus _bus;
    private readonly ILogger _logger;

    public TradeStateMachine(
        ShipModel ship,
        ShipAssignmentDto assignment,
        IShipAssignmentRepository assignments,
        IMessageBus bus,
        ILogger logger)
    {
        _ship = ship;
        _assignment = assignment;
        _assignments = assignments;
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
                    var units = _ship.CargoCapacity - _ship.CargoCurrent;
                    if (units > 0)
                        await _bus.SendAsync(new BuyCargoCommand(_ship.Symbol, _assignment.CargoSymbol, units));
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
                    await _bus.SendAsync(new SellCargoCommand(_ship.Symbol, _assignment.CargoSymbol, _ship.CargoCurrent));
                await MarkCompleteAsync(cancellationToken);
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
        var completed = _assignment with { CompletedAt = DateTimeOffset.UtcNow };
        await _assignments.UpsertAsync(completed, cancellationToken);
    }
}
