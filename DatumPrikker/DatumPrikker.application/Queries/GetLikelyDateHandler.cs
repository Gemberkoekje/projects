namespace DatumPrikker.application.Queries;

/// <summary>
/// Shows a respondent which date leads, only once they answered themselves, so a first answer isn't
/// swayed (D21). It hands out the leading tallies, never the answers behind them.
/// </summary>
public sealed class GetLikelyDateHandler(GetPollHandler polls, IAnswerStore answers)
{
    /// <summary>The refusal for somebody who hasn't answered this poll (yet).</summary>
    public const string AnswerFirst = "Je ziet de vermoedelijke datum zodra je zelf hebt geantwoord.";

    private readonly GetPollHandler polls = polls ?? throw new ArgumentNullException(nameof(polls));
    private readonly IAnswerStore answers = answers ?? throw new ArgumentNullException(nameof(answers));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Which poll, and the address the asker answered with.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The likely date, <see cref="AnswerFirst"/>, or <see cref="IPollStore.NotFound"/>.</returns>
    public Task<Result<LikelyDate>> HandleAsync(GetLikelyDate query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Email.TryNormalise(query.Email, out var email)
            ? TallyAsync(query.ShareToken, email, cancellationToken)
            : Task.FromResult(Result.WithMessages<LikelyDate>(ValidationMessage.Error(AnswerFirst, nameof(query.Email))));
    }

    private async Task<Result<LikelyDate>> TallyAsync(string shareToken, string email, CancellationToken cancellationToken)
    {
        var poll = await polls.HandleAsync(new GetPoll { ShareToken = shareToken }, cancellationToken);
        if (!poll.IsValid)
        {
            return Result.WithMessages<LikelyDate>(poll.Messages);
        }

        var given = await answers.ListForPollAsync(poll.Value.Id, cancellationToken);
        var own = Answer.IdFor(poll.Value.Id, email);
        if (!given.Any(answer => answer.Id == own))
        {
            return Result.WithMessages<LikelyDate>(ValidationMessage.Error(AnswerFirst, nameof(GetLikelyDate.Email)));
        }

        var results = PollResults.Tally(poll.Value, given);
        return new LikelyDate { Leading = results.Best, AnswerCount = results.Answers.Count };
    }
}
