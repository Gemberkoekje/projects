namespace DatumPrikker.application.Queries;

/// <summary>Loads the poll behind a link; malformed tokens never reach the store.</summary>
public sealed class GetPollHandler(IPollStore polls)
{
    private readonly IPollStore polls = polls ?? throw new ArgumentNullException(nameof(polls));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Which poll.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The poll, or <see cref="IPollStore.NotFound"/>.</returns>
    public Task<Result<Poll>> HandleAsync(GetPoll query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ShareToken.IsWellFormed(query.ShareToken)
            ? polls.LoadAsync(query.ShareToken, cancellationToken)
            : Task.FromResult(Result.WithMessages<Poll>(ValidationMessage.Error(IPollStore.NotFound, nameof(query.ShareToken))));
    }
}
