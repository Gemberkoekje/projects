using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Posts operator alerts to the webhook (<see cref="IAlertNotifier"/>) for:
/// <list type="bullet">
///   <item>a contract deadline that is approaching (≤ 6 h remaining);</item>
///   <item>an upcoming server reset;</item>
///   <item>cache divergence;</item>
///   <item>a token whose reset date doesn't match the server's.</item>
/// </list>
/// Only the last one is published at runtime. There is no credit-drop alert (D12): credits only drop
/// when the bot spends them, so it could only have reported the bot's own spending.
/// </summary>
public sealed class AlertHandler(
    IAlertNotifier alertNotifier,
    ILogger<AlertHandler> logger)
{
    public async Task Handle(ContractDeadlineApproachingEvent @event, CancellationToken cancellationToken)
    {
        if (@event.Remaining <= TimeSpan.FromHours(6))
        {
            var message =
                $"Contract {@event.ContractId} deadline is in {(int)@event.Remaining.TotalHours}h " +
                $"{@event.Remaining.Minutes}m.";

            logger.LogWarning("Contract deadline alert: {Message}", message);
            await alertNotifier.NotifyAsync("Contract Deadline Approaching", message, cancellationToken);
        }
    }

    public async Task Handle(ServerResetWarningEvent @event, CancellationToken cancellationToken)
    {
        var message =
            $"Server reset scheduled at {@event.NextResetAt:u} (in {(int)@event.Remaining.TotalHours}h {@event.Remaining.Minutes}m).";

        logger.LogWarning("Server reset warning alert: {Message}", message);
        await alertNotifier.NotifyAsync("Server Reset Upcoming", message, cancellationToken);
    }

    public async Task Handle(CacheDivergenceDetectedEvent @event, CancellationToken cancellationToken)
    {
        logger.LogWarning("Cache divergence alert: {Summary}", @event.Summary);
        await alertNotifier.NotifyAsync("Cache Divergence Detected", @event.Summary, cancellationToken);
    }

    public async Task Handle(TokenResetMismatchDetectedEvent @event, CancellationToken cancellationToken)
    {
        var message = $"Token reset-date mismatch detected for '{@event.Source}' token source. Automation will re-bootstrap a valid agent token.";
        logger.LogWarning("Token reset mismatch alert: {Message}", message);
        await alertNotifier.NotifyAsync("Token Reset Mismatch", message, cancellationToken);
    }
}
