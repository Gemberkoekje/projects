namespace DatumPrikker.application.Ports;

/// <summary>Keeps the <see cref="Access"/> documents, one per account.</summary>
public interface IAccessStore
{
    /// <summary>Loads one account's access.</summary>
    /// <param name="accountId">The account id.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The access, or an error when the account never asked.</returns>
    Task<Result<Access>> LoadAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>Every account that ever asked.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The accesses, in no particular order.</returns>
    Task<IReadOnlyList<Access>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>How many requests are waiting.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of accounts in <see cref="AccessState.Requested"/>.</returns>
    Task<int> CountRequestedAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores a new or changed access.</summary>
    /// <param name="access">The access.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>OK, or why it could not be stored.</returns>
    Task<Result> SaveAsync(Access access, CancellationToken cancellationToken = default);
}
