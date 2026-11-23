namespace Klaverjas.application.Ports;

/// <summary>Keeps Dad's preparation of each evening (D12).</summary>
public interface IVoorbereidingStore
{
    /// <summary>One evening's preparation.</summary>
    /// <param name="datum">The evening.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The preparation, or an error when the evening was not prepared.</returns>
    Task<Result<Voorbereiding>> LadenAsync(DateOnly datum, CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces an evening's preparation.</summary>
    /// <param name="voorbereiding">The preparation.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes once stored.</returns>
    Task OpslaanAsync(Voorbereiding voorbereiding, CancellationToken cancellationToken = default);
}
