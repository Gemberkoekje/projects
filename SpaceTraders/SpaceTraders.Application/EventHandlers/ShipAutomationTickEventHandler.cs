using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Planning;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Phase 5: single ship automation handler. Receives the explicit
/// <see cref="ShipAutomationTickEvent"/> and delegates to the goal executor service so exactly
/// one command (or none) is issued per decision point.
/// Phase 10: delegates to <see cref="IShipGoalExecutorService"/> which routes to the goal
/// executor when an active goal exists, and falls back to the legacy planner otherwise.
/// </summary>
public sealed class ShipAutomationTickEventHandler
{
    private readonly IShipGoalExecutorService _goalExecutorService;
    private readonly ILogger<ShipAutomationTickEventHandler> _logger;

    public ShipAutomationTickEventHandler(
        IShipGoalExecutorService goalExecutorService,
        ILogger<ShipAutomationTickEventHandler> logger)
    {
        _goalExecutorService = goalExecutorService;
        _logger = logger;
    }

    /// <summary>
    /// Phase 10 backward-compatibility constructor: wraps a legacy <see cref="IShipPlannerService"/>
    /// in a <see cref="LegacyPlannerGoalExecutorAdapter"/> so existing test wiring continues to work.
    /// </summary>
    [Obsolete("Phase 10: inject IShipGoalExecutorService directly. Will be removed in Phase 12.")]
    public ShipAutomationTickEventHandler(
        IShipPlannerService plannerService,
        ILogger<ShipAutomationTickEventHandler> logger)
        : this(new LegacyPlannerGoalExecutorAdapter(plannerService), logger)
    {
    }

    public async Task Handle(ShipAutomationTickEvent @event, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "ShipAutomationTick: planning ship {Ship} (reason={Reason}).",
            @event.ShipSymbol,
            string.IsNullOrWhiteSpace(@event.Reason) ? "(unspecified)" : @event.Reason);

        var result = await _goalExecutorService.ExecuteAsync(@event.ShipSymbol, cancellationToken);

        if (result is not null)
        {
            _logger.LogInformation(
                "ShipAutomationTick: ship={Ship} outcome={Outcome} reason={Reason}",
                @event.ShipSymbol,
                result.Outcome,
                result.Reason);
        }
    }
}

/// <summary>
/// Phase 10: adapts the legacy <see cref="IShipPlannerService"/> to the new
/// <see cref="IShipGoalExecutorService"/> contract so backward-compatible test wiring works.
/// </summary>
internal sealed class LegacyPlannerGoalExecutorAdapter(IShipPlannerService plannerService) : IShipGoalExecutorService
{
    public async Task<GoalExecutionResult?> ExecuteAsync(string shipSymbol, CancellationToken ct)
    {
        await plannerService.PlanAndExecuteAsync(shipSymbol, ct);
        return null;
    }
}
