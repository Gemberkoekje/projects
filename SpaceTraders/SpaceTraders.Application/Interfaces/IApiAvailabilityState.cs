namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Tracks whether the SpaceTraders API is currently reachable.
/// Set by the HTTP retry handler; read by the GameLoopService to publish domain events.
/// </summary>
public interface IApiAvailabilityState
{
    bool IsAvailable { get; }

    /// <summary>
    /// Returns true if the availability state changed from available to unavailable
    /// since the last call to <see cref="ConsumeUnavailableTransition"/>.
    /// </summary>
    bool ConsumeUnavailableTransition();

    /// <summary>
    /// Returns true if the availability state changed from unavailable to available
    /// since the last call to <see cref="ConsumeAvailableTransition"/>.
    /// </summary>
    bool ConsumeAvailableTransition();

    void MarkUnavailable();

    void MarkAvailable();
}
