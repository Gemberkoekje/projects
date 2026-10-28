namespace Kluskat.application.Ports;

/// <summary>Keeps the one <see cref="Sessie"/>: what was skipped lately, and the "Geen tijd" cap.</summary>
public interface ISessieStore
{
    /// <summary>The stored sessie, or a fresh one when there is none yet. Not yet checked for expiry.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The sessie as stored.</returns>
    Task<Sessie> LadenAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored sessie.</summary>
    /// <param name="sessie">The new state.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once stored.</returns>
    Task OpslaanAsync(Sessie sessie, CancellationToken cancellationToken = default);
}
