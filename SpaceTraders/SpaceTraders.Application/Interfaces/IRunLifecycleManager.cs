namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Manages the run lifecycle: opens runs on startup, rotates runs when strategy-relevant
/// settings change, and promotes scheduled runs when their activation time arrives.
/// </summary>
public interface IRunLifecycleManager
{
    /// <summary>
    /// Called when a setting is changed via the operator API. If <paramref name="changedKey"/>
    /// is strategy-relevant the current run is closed and a new run is opened atomically.
    /// </summary>
    Task RotateForSettingsChangeAsync(string changedKey, CancellationToken cancellationToken = default);
}
