namespace DatumPrikker.application.Queries;

/// <summary>Loads the answer one email address gave to one poll.</summary>
public sealed class GetAnswerHandler(IAnswerStore answers)
{
    private readonly IAnswerStore answers = answers ?? throw new ArgumentNullException(nameof(answers));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Which poll and address.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The answer, or an error when there is none.</returns>
    public Task<Result<Answer>> HandleAsync(GetAnswer query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Email.TryNormalise(query.Email, out var email)
            ? answers.LoadAsync(Answer.IdFor(query.PollId, email), cancellationToken)
            : Task.FromResult(Result.WithMessages<Answer>(ValidationMessage.Error("Geen geldig e-mailadres.", nameof(query.Email))));
    }
}
