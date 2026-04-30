using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// Phase 6.5e helper: publishes a <see cref="ShipStateMismatchEvent"/> together with
/// a <see cref="ShipAutomationTickEvent"/> so the planner can reassert the next valid
/// action without depending on the chain-of-command fallback handler.
/// </summary>
internal static class ShipStateMismatchPublisher
{
    public static async Task PublishMismatchAndTickAsync(
        this IMessageBus bus,
        string shipSymbol,
        string commandName,
        string requiredState,
        string actualState,
        string reason)
    {
        var now = TimeProvider.System.GetUtcNow();

        await bus.PublishAsync(new ShipStateMismatchEvent(
            shipSymbol,
            commandName,
            requiredState,
            actualState,
            reason,
            Guid.Empty,
            Guid.Empty,
            now));

        // Phase 6.5e: ensure planner recovery does not depend on the chain fallback handler.
        await bus.PublishAsync(new ShipAutomationTickEvent(
            shipSymbol,
            $"StateMismatch:{commandName}",
            now,
            Guid.NewGuid(),
            Guid.Empty));
    }
}
