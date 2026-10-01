namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Tracks whether the SpaceTraders API is currently reachable, and the pause after a 502.
/// Set by the HTTP outage handler; read by the GameLoopService, which skips its work during a pause
/// and publishes the availability transitions.
/// </summary>
public interface IApiAvailabilityState
{
    bool IsAvailable { get; }

    /// <summary>No API call goes out before this time. <see cref="DateTimeOffset.MinValue"/> when there is no pause.</summary>
    DateTimeOffset PausedUntil { get; }

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

    /// <summary>Marks the API unavailable and pauses all API calls until <paramref name="until"/>.</summary>
    void PauseUntil(DateTimeOffset until);

    void MarkAvailable();
}
