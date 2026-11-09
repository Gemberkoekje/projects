namespace DatumPrikker.application.Queries;

/// <summary>Combines the admin list and the stored access into one status.</summary>
public sealed class GetAccessHandler(IAccessStore access, Admins admins)
{
    private readonly IAccessStore access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly Admins admins = admins ?? throw new ArgumentNullException(nameof(admins));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Which account.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The status; somebody anonymous can never make polls.</returns>
    public Task<AccessStatus> HandleAsync(GetAccess query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return query.Account.IsAnonymous
            ? Task.FromResult(new AccessStatus())
            : LoadAsync(query.Account, cancellationToken);
    }

    private async Task<AccessStatus> LoadAsync(Account account, CancellationToken cancellationToken)
    {
        var stored = await access.LoadAsync(account.Id, cancellationToken);
        return new AccessStatus
        {
            Account = account,
            IsAdmin = admins.Includes(account),
            Access = stored.IsValid ? stored.Value : new Access(),
        };
    }
}
