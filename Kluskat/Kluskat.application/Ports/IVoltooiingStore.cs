namespace Kluskat.application.Ports;

/// <summary>
/// The log of klussen ticked off on the klus screen: what XP, levels, the streak and the
/// achievements are computed from. It only ever counts what was done, never what is left.
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

    /// <summary>The whole log. A few thousand small documents at most, so the rules score it in memory.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Every completion, in no particular order.</returns>
    Task<IReadOnlyList<Voltooiing>> AlleAsync(CancellationToken cancellationToken = default);
}
