namespace Kluskat.application.Ports;

/// <summary>Keeps the one <see cref="Profiel"/>.</summary>
public interface IProfielStore
{
    /// <summary>The stored profile, or a fresh one when there is none yet.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The profile.</returns>
    Task<Profiel> LadenAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored profile.</summary>
    /// <param name="profiel">The new state.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once stored.</returns>
    Task OpslaanAsync(Profiel profiel, CancellationToken cancellationToken = default);
}
