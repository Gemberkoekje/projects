namespace DatumPrikker.application.Queries;

/// <summary>Tallies a poll for its owner; anybody else gets a refusal.</summary>
public sealed class GetResultsHandler(GetPollHandler polls, IAnswerStore answers, ILogger<GetResultsHandler> logger)
{
    /// <summary>The refusal for anybody but the owner.</summary>
    public const string OwnerOnly = "Alleen de maker van deze prikker ziet de uitslag.";

    private readonly GetPollHandler polls = polls ?? throw new ArgumentNullException(nameof(polls));
    private readonly IAnswerStore answers = answers ?? throw new ArgumentNullException(nameof(answers));
    private readonly ILogger<GetResultsHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Which poll, and who asks.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The results, or why they are refused.</returns>
    public Task<Result<PollResults>> HandleAsync(GetResults query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return TallyAsync(query, cancellationToken);
    }

    private async Task<Result<PollResults>> TallyAsync(GetResults query, CancellationToken cancellationToken)
    {
        var poll = await polls.HandleAsync(new GetPoll { ShareToken = query.ShareToken }, cancellationToken);
        if (!poll.IsValid)
        {
            return Result.WithMessages<PollResults>(poll.Messages);
        }

        if (!poll.Value.IsOwnedBy(query.By))
        {
            logger.LogWarning("Account {AccountId} asked for the results of poll {PollId}, which it did not make.", query.By.Id, poll.Value.Id);
            return Result.WithMessages<PollResults>(ValidationMessage.Error(OwnerOnly, nameof(query.By)));
        }

        var given = await answers.ListForPollAsync(poll.Value.Id, cancellationToken);
        return PollResults.Tally(poll.Value, given);
    }
}
