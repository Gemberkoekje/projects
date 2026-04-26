using Stateless;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;
using System;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Stateless state machine for the Mine assignment.
///
/// States (map 1:1 to persisted StepIndex):
///   NavigateToAsteroid = 0
///   OrbitAtAsteroid    = 1
///   ExtractResources   = 2
///   NavigateToSell     = 3
///   DockAtSell         = 4
///   SellCargo          = 5
///   Completed          = 6
/// </summary>
internal sealed class MineStateMachine
{
    private const double MineCargoThreshold = 0.9;

    internal enum MineStep
    {
        None = -1,
        NavigateToAsteroid = 0,
        OrbitAtAsteroid = 1,
        ExtractResources = 2,
        NavigateToSell = 3,
        DockAtSell = 4,
        SellCargo = 5,
        Completed = 6,
    }

    private enum Trigger { Advance, Complete }

    private readonly StateMachine<MineStep, Trigger> _machine;
    private readonly ShipModel _ship;
    private readonly ShipAssignmentDto _assignment;
    private readonly IShipAssignmentRepository _assignments;
    private readonly IMessageBus _bus;
    private readonly ILogger _logger;

    public MineStateMachine(
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

        var initialState = assignment.StepIndex >= 0 && assignment.StepIndex <= (int)MineStep.Completed
            ? (MineStep)assignment.StepIndex
            : MineStep.Completed;

        _machine = new StateMachine<MineStep, Trigger>(initialState);

        _machine.Configure(MineStep.NavigateToAsteroid).Permit(Trigger.Advance, MineStep.OrbitAtAsteroid);
        _machine.Configure(MineStep.OrbitAtAsteroid).Permit(Trigger.Advance, MineStep.ExtractResources);
        _machine.Configure(MineStep.ExtractResources)
            .PermitReentry(Trigger.Advance)
            .Permit(Trigger.Complete, MineStep.NavigateToSell);
        _machine.Configure(MineStep.NavigateToSell).Permit(Trigger.Advance, MineStep.DockAtSell);
        _machine.Configure(MineStep.DockAtSell).Permit(Trigger.Advance, MineStep.SellCargo);
        _machine.Configure(MineStep.SellCargo).Permit(Trigger.Complete, MineStep.Completed);
        _machine.Configure(MineStep.Completed).OnEntry(() => { });
    }

    public async Task AdvanceAsync(CancellationToken cancellationToken)
    {
        switch (_machine.State)
        {
            case MineStep.NavigateToAsteroid:
                if (_assignment.OriginWaypoint is null)
                {
                    _logger.LogWarning("Ship {Symbol} Mine assignment missing OriginWaypoint. Marking complete.", _ship.Symbol);
                    await MarkCompleteAsync(cancellationToken);
                    return;
                }
                if (!IsAtWaypoint(_assignment.OriginWaypoint))
                    await _bus.SendAsync(new NavigateShipCommand(_ship.Symbol, _assignment.OriginWaypoint));
                await PersistStepAsync(Trigger.Advance, cancellationToken);
                break;

            case MineStep.OrbitAtAsteroid:
                if (IsAtWaypoint(_assignment.OriginWaypoint) && IsDocked())
                    await _bus.SendAsync(new OrbitShipCommand(_ship.Symbol));
                await PersistStepAsync(Trigger.Advance, cancellationToken);
                break;

            case MineStep.ExtractResources:
                if (IsCargoFull())
                {
                    _logger.LogInformation("Ship {Symbol} cargo ≥ 90 %. Advancing to navigate to sell market.", _ship.Symbol);
                    await PersistStepAsync(Trigger.Complete, cancellationToken);
                }
                else if (IsInOrbit() && IsAtWaypoint(_assignment.OriginWaypoint))
                {
                    await _bus.SendAsync(new ExtractResourcesCommand(_ship.Symbol));
                }
                break;

            case MineStep.NavigateToSell:
                if (_assignment.DestWaypoint is null)
                {
                    _logger.LogWarning("Ship {Symbol} Mine assignment missing DestWaypoint. Marking complete.", _ship.Symbol);
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
                await PersistStepAsync(Trigger.Advance, cancellationToken);
                break;

            case MineStep.DockAtSell:
                if (IsAtWaypoint(_assignment.DestWaypoint) && !IsDocked())
                    await _bus.SendAsync(new DockShipCommand(_ship.Symbol));
                await PersistStepAsync(Trigger.Advance, cancellationToken);
                break;

            case MineStep.SellCargo:
                if (_assignment.CargoSymbol is not null && _ship.CargoCurrent > 0 && IsDocked())
                {
                    await _bus.SendAsync(new SellCargoCommand(_ship.Symbol, _assignment.CargoSymbol, _ship.CargoCurrent));
                    await MarkCompleteOnlyAsync(cancellationToken);
                }
                else if (_assignment.CargoSymbol is null && _ship.CargoCurrent > 0)
                {
                    _logger.LogWarning("Ship {Symbol} Mine step SellCargo: no CargoSymbol set, skipping sell.", _ship.Symbol);
                    await MarkCompleteAsync(cancellationToken);
                }
                else
                {
                    await MarkCompleteAsync(cancellationToken);
                }
                _logger.LogInformation("Ship {Symbol} completed Mine assignment.", _ship.Symbol);
                break;

            default:
                _logger.LogWarning("Ship {Symbol} has unexpected Mine step {Step}. Marking complete.", _ship.Symbol, _machine.State);
                await MarkCompleteAsync(cancellationToken);
                break;
        }
    }

    private bool IsAtWaypoint(string? waypointSymbol)
        => waypointSymbol is not null &&
           _ship.WaypointSymbol?.Equals(waypointSymbol, StringComparison.OrdinalIgnoreCase) == true;

    private bool IsDocked()
        => _ship.Status?.Equals("DOCKED", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsInOrbit()
        => _ship.Status?.Equals("IN_ORBIT", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsCargoFull()
    {
        if (_ship.CargoCapacity == 0) return false;
        return (double)_ship.CargoCurrent / _ship.CargoCapacity >= MineCargoThreshold;
    }

    private async Task PersistStepAsync(Trigger trigger, CancellationToken cancellationToken)
    {
        await _machine.FireAsync(trigger);
        var updated = _assignment with { StepIndex = (int)_machine.State };
        await _assignments.UpsertAsync(updated, cancellationToken);
    }

    private async Task MarkCompleteAsync(CancellationToken cancellationToken)
    {
        await MarkCompleteAsync(AssignmentType.Mine, cancellationToken);
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
}
