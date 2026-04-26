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
/// Stateless state machine for the Scout assignment.
///
/// States (map 1:1 to persisted StepIndex):
///   NavigateToTarget = 0
///   RefreshData      = 1
///   Completed        = 2
/// </summary>
internal sealed class ScoutStateMachine
{
    internal enum ScoutStep
    {
        None = -1,
        NavigateToTarget = 0,
        RefreshData = 1,
        Completed = 2,
    }

    private enum Trigger { Advance, Complete }

    private readonly StateMachine<ScoutStep, Trigger> _machine;
    private readonly ShipModel _ship;
    private readonly ShipAssignmentDto _assignment;
    private readonly IShipAssignmentRepository _assignments;
    private readonly IMessageBus _bus;
    private readonly ILogger _logger;

    public ScoutStateMachine(
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

        var initialState = assignment.StepIndex >= 0 && assignment.StepIndex <= (int)ScoutStep.Completed
            ? (ScoutStep)assignment.StepIndex
            : ScoutStep.Completed;

        _machine = new StateMachine<ScoutStep, Trigger>(initialState);

        _machine.Configure(ScoutStep.NavigateToTarget).Permit(Trigger.Advance, ScoutStep.RefreshData);
        _machine.Configure(ScoutStep.RefreshData).Permit(Trigger.Complete, ScoutStep.Completed);
        _machine.Configure(ScoutStep.Completed).OnEntry(() => { });
    }

    public async Task AdvanceAsync(CancellationToken cancellationToken)
    {
        switch (_machine.State)
        {
            case ScoutStep.NavigateToTarget:
                if (_assignment.OriginWaypoint is null)
                {
                    _logger.LogWarning("Ship {Symbol} Scout assignment missing OriginWaypoint. Marking complete.", _ship.Symbol);
                    await MarkCompleteAsync(cancellationToken);
                    return;
                }
                if (!IsAtWaypoint(_assignment.OriginWaypoint))
                    await _bus.SendAsync(new NavigateShipCommand(_ship.Symbol, _assignment.OriginWaypoint));
                await PersistStepAsync(Trigger.Advance, cancellationToken);
                break;

            case ScoutStep.RefreshData:
                if (_assignment.OriginWaypoint is not null && IsAtWaypoint(_assignment.OriginWaypoint))
                {
                    var systemSymbol = ExtractSystemSymbol(_assignment.OriginWaypoint);
                    await _bus.SendAsync(new RefreshMarketDataCommand(systemSymbol, _assignment.OriginWaypoint));
                    await _bus.SendAsync(new RefreshShipyardDataCommand(systemSymbol, _assignment.OriginWaypoint));
                }
                await MarkCompleteAsync(cancellationToken);
                _logger.LogInformation("Ship {Symbol} completed Scout assignment at {Waypoint}.",
                    _ship.Symbol, _assignment.OriginWaypoint);
                break;

            default:
                _logger.LogWarning("Ship {Symbol} has unexpected Scout step {Step}. Marking complete.", _ship.Symbol, _machine.State);
                await MarkCompleteAsync(cancellationToken);
                break;
        }
    }

    private bool IsAtWaypoint(string? waypointSymbol)
        => waypointSymbol is not null &&
           _ship.WaypointSymbol?.Equals(waypointSymbol, StringComparison.OrdinalIgnoreCase) == true;

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }

    private async Task PersistStepAsync(Trigger trigger, CancellationToken cancellationToken)
    {
        await _machine.FireAsync(trigger);
        var updated = _assignment with { StepIndex = (int)_machine.State };
        await _assignments.UpsertAsync(updated, cancellationToken);
    }

    private async Task MarkCompleteAsync(CancellationToken cancellationToken)
    {
        await MarkCompleteAsync(AssignmentType.Scout, cancellationToken);
    }

    private async Task MarkCompleteAsync(AssignmentType completedType, CancellationToken cancellationToken)
    {
        var completed = _assignment with { CompletedAt = TimeProvider.System.GetUtcNow() };
        await _assignments.UpsertAsync(completed, cancellationToken);
        await _bus.PublishAsync(new ShipAssignmentCompletedEvent(_ship.Symbol, completedType));
    }
}
