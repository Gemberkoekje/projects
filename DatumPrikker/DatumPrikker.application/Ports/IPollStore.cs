namespace DatumPrikker.application.Ports;

/// <summary>Keeps the <see cref="Poll"/> documents.</summary>
public interface IPollStore
{
    /// <summary>The message for a share token that matches no poll.</summary>
    const string NotFound = "Deze prikker bestaat niet (meer).";

    /// <summary>Loads the poll with this share token.</summary>
    /// <param name="shareToken">The code from the poll's link.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The poll, or <see cref="NotFound"/>.</returns>
    Task<Result<Poll>> LoadAsync(string shareToken, CancellationToken cancellationToken = default);

    /// <summary>True when some poll already uses this share token.</summary>
    /// <param name="shareToken">The candidate token.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>True when taken.</returns>
    Task<bool> IsShareTokenTakenAsync(string shareToken, CancellationToken cancellationToken = default);

    /// <summary>Every poll one account made.</summary>
    /// <param name="accountId">The account (see <see cref="Account.Id"/>).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The polls, in no particular order.</returns>
    Task<IReadOnlyList<Poll>> ListOwnedByAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>The polls with these ids; ids without a poll are skipped.</summary>
    /// <param name="ids">The poll ids.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The polls found, in no particular order.</returns>
    Task<IReadOnlyList<Poll>> LoadManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Stores a new or changed poll.</summary>
    /// <param name="poll">The poll.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>OK, or why it could not be stored.</returns>
    Task<Result> SaveAsync(Poll poll, CancellationToken cancellationToken = default);
}
