namespace Kluskat.application.Ports;

/// <summary>Loads and saves <see cref="Klus"/> aggregates; saving also refreshes the read model.</summary>
public interface IKlusRepository
{
    /// <summary>Replays a klus from its stream.</summary>
    /// <param name="id">The klus.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The klus, or a message when it does not exist.</returns>
    Task<Result<Klus>> LadenAsync(KlusId id, CancellationToken cancellationToken = default);

    /// <summary>Appends the buffered events and refreshes the <see cref="KlusOverzicht"/>.</summary>
    /// <param name="klus">The klus with uncommitted events.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>OK, or a message when the stream moved on in the meantime.</returns>
    Task<Result> OpslaanAsync(Klus klus, CancellationToken cancellationToken = default);
}
