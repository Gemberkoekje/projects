namespace Klaverjas.application.Ports;

/// <summary>Keeps every <see cref="Verzending"/>: the send log.</summary>
public interface IVerzendingStore
{
    /// <summary>Every mailing, oldest first.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The mailings.</returns>
    Task<IReadOnlyList<Verzending>> AllesAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores a mailing.</summary>
    /// <param name="verzending">The mailing.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once stored.</returns>
    Task OpslaanAsync(Verzending verzending, CancellationToken cancellationToken = default);
}
