namespace Kluskat.application.Ports;

/// <summary>Reads the <see cref="KlusOverzicht"/> documents the picker and the beheer page work from.</summary>
public interface IKlusOverzichtStore
{
    /// <summary>Every klus that is still on the list.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The active klussen, in no particular order.</returns>
    Task<IReadOnlyList<KlusOverzicht>> ActieveAsync(CancellationToken cancellationToken = default);

    /// <summary>One klus, geschrapt or not.</summary>
    /// <param name="id">The klus.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The klus, or a message when it does not exist.</returns>
    Task<Result<KlusOverzicht>> LadenAsync(KlusId id, CancellationToken cancellationToken = default);

    /// <summary>Whether any klus was ever stored, geschrapt ones included.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><see langword="true"/> once the list has been seeded.</returns>
    Task<bool> HeeftKlussenAsync(CancellationToken cancellationToken = default);
}
