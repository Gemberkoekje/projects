namespace Kluskat.application.Ports;

/// <summary>
/// The log of klussen ticked off on the klus screen: what the win screen counts, and what phase 2
/// computes XP and the weekly streak from. It only ever counts what was done, never what is left.
/// </summary>
public interface IVoltooiingStore
{
    /// <summary>Logs a completion.</summary>
    /// <param name="voltooiing">The completion.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once stored.</returns>
    Task ToevoegenAsync(Voltooiing voltooiing, CancellationToken cancellationToken = default);

    /// <summary>Removes the latest completion of a klus, after its "Gedaan" was taken back.</summary>
    /// <param name="id">The klus.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>A task that completes once removed.</returns>
    Task LaatsteVerwijderenAsync(KlusId id, CancellationToken cancellationToken = default);

    /// <summary>How many completions were logged since a moment.</summary>
    /// <param name="sinds">The start (UTC), inclusive.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count.</returns>
    Task<int> AantalSindsAsync(DateTime sinds, CancellationToken cancellationToken = default);
}
