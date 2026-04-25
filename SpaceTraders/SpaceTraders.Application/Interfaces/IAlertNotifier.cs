namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Sends operator notifications for significant game events (credit drop, contract failure, fleet cap).
/// </summary>
public interface IAlertNotifier
{
    /// <summary>
    /// Sends an alert with the given title and message.
    /// Implementations should be fire-and-forget safe – failures must not propagate.
    /// </summary>
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default);
}
