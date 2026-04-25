namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Indicates whether this application instance currently holds the leader lease.
/// Only the leader should run exclusive background services such as <c>GameLoopService</c>.
/// </summary>
public interface ILeaderElection
{
    /// <summary>
    /// Returns <c>true</c> when this instance holds a valid leader lease.
    /// Thread-safe; may be polled on every tick without additional locking.
    /// </summary>
    bool IsLeader { get; }
}
