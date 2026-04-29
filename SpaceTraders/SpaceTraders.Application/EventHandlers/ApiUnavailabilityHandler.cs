using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Handles API availability changes:
/// - On <see cref="ApiUnavailableEvent"/>: disables automation and schedules a probe every 30 s.
/// - On <see cref="ApiAvailableEvent"/>: re-enables automation.
/// </summary>
public sealed class ApiUnavailabilityHandler(
    ISettingsRepository settings,
    IMessageBus bus,
    ILogger<ApiUnavailabilityHandler> logger)
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(30);

    public async Task Handle(ApiUnavailableEvent @event, CancellationToken cancellationToken)
    {
        logger.LogWarning("SpaceTraders API unavailable since {DetectedAt}. Disabling automation.", @event.DetectedAt);

        await settings.SetAsync("Automation.Enabled", "false", cancellationToken);
        await settings.SetAsync("Runtime.ApiUnavailable", "true", cancellationToken);
        await settings.SetAsync("Runtime.Alert.ApiUnavailable", "true", cancellationToken);
        await settings.SetAsync("Runtime.Alert.AutomationDisabled", "true", cancellationToken);

        // Schedule a connectivity probe every 30 s until the API recovers.
        await bus.ScheduleAsync(new ApiAvailabilityProbeCommand(), ProbeInterval);
    }

    public async Task Handle(ApiAvailableEvent @event, CancellationToken cancellationToken)
    {
        logger.LogInformation("SpaceTraders API available again at {RestoredAt}. Re-enabling automation.", @event.RestoredAt);

        await settings.SetAsync("Automation.Enabled", "true", cancellationToken);
        await settings.SetAsync("Runtime.ApiUnavailable", "false", cancellationToken);
        await settings.SetAsync("Runtime.Alert.ApiUnavailable", "false", cancellationToken);
        await settings.SetAsync("Runtime.Alert.AutomationDisabled", "false", cancellationToken);
    }
}
