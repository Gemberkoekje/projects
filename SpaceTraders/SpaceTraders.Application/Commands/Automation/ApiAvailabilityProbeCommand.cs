using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Automation;

public record ApiAvailabilityProbeCommand;

/// <summary>
/// Scheduled probe command dispatched when the SpaceTraders API becomes unavailable.
/// Calls GET / (server status) to test reachability. On success, publishes
/// <see cref="ApiAvailableEvent"/> so automation can be re-enabled.
/// If the probe fails the command is retried by Wolverine's retry policy.
/// </summary>
public sealed class ApiAvailabilityProbeHandler(
    ISpaceTradersPort port,
    IMessageBus bus,
    ILogger<ApiAvailabilityProbeHandler> logger)
{
    public async Task Handle(ApiAvailabilityProbeCommand command, CancellationToken cancellationToken)
    {
        try
        {
            // A successful call to any port method indicates the API is reachable.
            // GetMyAgentAsync is lightweight and requires auth – but the intent is
            // just connectivity; any 200 response clears the unavailable state.
            await port.GetMyAgentAsync(cancellationToken);

            logger.LogInformation("SpaceTraders API probe succeeded; restoring availability.");
            await bus.PublishAsync(new ApiAvailableEvent(DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "SpaceTraders API probe failed; will retry.");
            throw; // Let Wolverine retry policy reschedule the command
        }
    }
}
