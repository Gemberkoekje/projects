using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Planning;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Phase 5: single ship automation handler. Receives the explicit
/// <see cref="ShipAutomationTickEvent"/> and delegates to the planner service so exactly one
/// command (or none) is issued per decision point. This replaces the chain-of-command
/// inheritance routing for automation purposes; the legacy chain handlers remain registered
/// during the migration so existing flows keep working until Phase 7 removes them.
/// </summary>
public sealed class ShipAutomationTickEventHandler(
    IShipPlannerService planner,
    ILogger<ShipAutomationTickEventHandler> logger)
{
    public async Task Handle(ShipAutomationTickEvent @event, CancellationToken cancellationToken)
    {
        logger.LogDebug(
            "ShipAutomationTick: planning ship {Ship} (reason={Reason}).",
            @event.ShipSymbol,
            string.IsNullOrWhiteSpace(@event.Reason) ? "(unspecified)" : @event.Reason);

        var decision = await planner.PlanAndExecuteAsync(@event.ShipSymbol, cancellationToken);

        logger.LogInformation(
            "ShipAutomationTick: ship={Ship} decision={Kind} reason={Reason}",
            @event.ShipSymbol,
            decision.Kind,
            decision.Reason);
    }
}
