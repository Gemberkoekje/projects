namespace Klaverjas.application.Ports;

/// <summary>Keeps the evenings' results.</summary>
public interface IAvondStore
{
    /// <summary>Every entered evening, of every season.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The evenings, in no particular order.</returns>
    Task<IReadOnlyList<Avond>> AllesAsync(CancellationToken cancellationToken = default);

    /// <summary>One evening.</summary>
    /// <param name="datum">The evening's date.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The evening, or an error when it has not been entered.</returns>
    Task<Result<Avond>> LadenAsync(DateOnly datum, CancellationToken cancellationToken = default);

    /// <summary>Stores an evening. Replacing one keeps the previous version (D8).</summary>
    /// <param name="avond">The evening.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once stored.</returns>
    Task OpslaanAsync(Avond avond, CancellationToken cancellationToken = default);
}
