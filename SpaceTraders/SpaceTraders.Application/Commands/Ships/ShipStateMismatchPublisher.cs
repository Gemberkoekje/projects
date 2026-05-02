using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// Helper: publishes a <see cref="ShipStateMismatchEvent"/> for diagnostics when a command
/// is issued in an invalid ship state. The mismatch event is logged and can be used to
/// detect bugs; the ship will be re-evaluated on the next orchestrator tick.
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
    }
}
