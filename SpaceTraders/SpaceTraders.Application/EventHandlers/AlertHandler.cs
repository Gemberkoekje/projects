using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.EventHandlers;

/// <summary>
/// Publishes operator alerts for critical game events:
/// <list type="bullet">
///   <item>Agent credits drop by more than 10 % between two consecutive readings.</item>
///   <item>A contract deadline is approaching (≤ 6 h remaining).</item>
///   <item>Fleet expansion is blocked because the hard cap has been reached.</item>
/// </list>
/// </summary>
public sealed class AlertHandler(
    IAlertNotifier alertNotifier,
    ILogger<AlertHandler> logger)
{
    private long _previousCredits = long.MinValue;

    public async Task Handle(AgentCreditsChangedEvent @event, CancellationToken cancellationToken)
    {
        if (_previousCredits != long.MinValue && _previousCredits > 0)
        {
            var dropFraction = (double)(_previousCredits - @event.NewCredits) / _previousCredits;
            if (dropFraction > 0.10)
            {
                var message =
                    $"Credits dropped from {_previousCredits:N0} → {@event.NewCredits:N0} " +
                    $"({dropFraction:P0} decrease).";

                logger.LogWarning("Credit drop alert: {Message}", message);
                await alertNotifier.NotifyAsync("Credit Drop Detected", message, cancellationToken);
            }
        }

        _previousCredits = @event.NewCredits;
    }

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
