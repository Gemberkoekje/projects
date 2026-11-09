namespace DatumPrikker.application.Queries;

/// <summary>
/// Suggests the maker's name: the one on their latest poll, so a name changed once sticks, or else
/// the name their sign-in provider gave.
/// </summary>
public sealed class GetMakerNameHandler(IPollStore polls)
{
    private readonly IPollStore polls = polls ?? throw new ArgumentNullException(nameof(polls));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Whose name.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The suggested name; empty for somebody anonymous.</returns>
    public Task<string> HandleAsync(GetMakerName query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return query.Account.IsAnonymous
            ? Task.FromResult(string.Empty)
            : SuggestAsync(query.Account, cancellationToken);
    }

    private async Task<string> SuggestAsync(Account account, CancellationToken cancellationToken)
    {
        var owned = await polls.ListOwnedByAsync(account.Id, cancellationToken);
        var latest = owned
            .Where(poll => poll.OwnerName.Length > 0)
            .OrderByDescending(poll => poll.CreatedOn)
            .FirstOrDefault();

        return latest?.OwnerName ?? account.Name;
    }
}
