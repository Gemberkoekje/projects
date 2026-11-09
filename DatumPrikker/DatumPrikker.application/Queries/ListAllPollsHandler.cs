namespace DatumPrikker.application.Queries;

/// <summary>Lists every poll, newest first, with its answer count; admins only.</summary>
public sealed class ListAllPollsHandler(IPollStore polls, IAnswerStore answers, Admins admins)
{
    private readonly IPollStore polls = polls ?? throw new ArgumentNullException(nameof(polls));
    private readonly IAnswerStore answers = answers ?? throw new ArgumentNullException(nameof(answers));
    private readonly Admins admins = admins ?? throw new ArgumentNullException(nameof(admins));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Who asks.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The polls, or <see cref="Admins.OnlyMessage"/>.</returns>
    public Task<Result<IReadOnlyList<PollOverview>>> HandleAsync(ListAllPolls query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return admins.Includes(query.By)
            ? ListAsync(cancellationToken)
            : Task.FromResult(Result.WithMessages<IReadOnlyList<PollOverview>>(admins.Require(query.By).Messages));
    }

    private async Task<Result<IReadOnlyList<PollOverview>>> ListAsync(CancellationToken cancellationToken)
    {
        var all = await polls.ListAllAsync(cancellationToken);
        var counts = await answers.CountForPollsAsync([.. all.Select(poll => poll.Id)], cancellationToken);

        IReadOnlyList<PollOverview> overview = [.. all
            .OrderByDescending(poll => poll.CreatedOn)
            .Select(poll => new PollOverview
            {
                Poll = poll,
                AnswerCount = counts.TryGetValue(poll.Id, out var count) ? count : 0,
            })];

        return Result.For(overview);
    }
}
