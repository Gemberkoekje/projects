using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Goals;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Phase 5: single ship automation handler. Receives the explicit
/// <see cref="ShipAutomationTickEvent"/> and delegates to the goal executor service so exactly
/// one command (or none) is issued per decision point.
/// Phase 10: delegates to <see cref="IShipGoalExecutorService"/> which routes to the goal executor.
/// Phase 12a: legacy planner constructor and adapter removed.
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
