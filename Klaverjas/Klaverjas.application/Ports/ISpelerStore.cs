namespace Klaverjas.application.Ports;

/// <summary>
/// Keeps the players, members and substitutes alike. A member is never deleted (D16); a substitute can
/// be, with their evenings (Dad, 2026-11-25: the workbook's nameless "Invaller (uit Excel)").
/// </summary>
public interface ISpelerStore
{
    /// <summary>Every player.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The players, in no particular order.</returns>
    Task<IReadOnlyList<Speler>> AllesAsync(CancellationToken cancellationToken = default);

    /// <summary>One player.</summary>
    /// <param name="id">The player's identity.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The player, or an error when there is none with that identity.</returns>
    Task<Result<Speler>> LadenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces a player.</summary>
    /// <param name="speler">The new state.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once stored.</returns>
    Task OpslaanAsync(Speler speler, CancellationToken cancellationToken = default);

    /// <summary>Removes a player.</summary>
    /// <param name="id">The player's identity.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes once removed.</returns>
    Task WeghalenAsync(Guid id, CancellationToken cancellationToken = default);
}
