namespace Klaverjas.application.Ports;

/// <summary>Keeps the one <see cref="Instellingen"/>.</summary>
public interface IInstellingenStore
{
    /// <summary>The stored settings, or the defaults when there are none yet.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The settings.</returns>
    Task<Instellingen> LadenAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored settings.</summary>
    /// <param name="instellingen">The new state.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once stored.</returns>
    Task OpslaanAsync(Instellingen instellingen, CancellationToken cancellationToken = default);
}
