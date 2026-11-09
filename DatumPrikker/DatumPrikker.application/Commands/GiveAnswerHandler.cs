namespace DatumPrikker.application.Commands;

/// <summary>Stores an answer; the same email address replaces its earlier answer (last write wins).</summary>
public sealed class GiveAnswerHandler(IPollStore polls, IAnswerStore answers, ILogger<GiveAnswerHandler> logger)
{
    private readonly IPollStore polls = polls ?? throw new ArgumentNullException(nameof(polls));
    private readonly IAnswerStore answers = answers ?? throw new ArgumentNullException(nameof(answers));
    private readonly ILogger<GiveAnswerHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The answer.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The stored answer, or why it was refused.</returns>
    public Task<Result<Answer>> HandleAsync(GiveAnswer command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return GiveAsync(command, cancellationToken);
    }

    private async Task<Result<Answer>> GiveAsync(GiveAnswer command, CancellationToken cancellationToken)
    {
        var poll = await polls.LoadAsync(command.ShareToken, cancellationToken);
        if (!poll.IsValid)
        {
            return Result.WithMessages<Answer>(poll.Messages);
        }

        var answer = Answer.Give(poll.Value, command.Draft, command.By, Clock.UtcNow());
        if (!answer.IsValid)
        {
            return answer;
        }

        var earlier = await answers.LoadAsync(answer.Value.Id, cancellationToken);
        var toStore = earlier.IsValid
            ? answer.Value with { CreatedOn = earlier.Value.CreatedOn }
            : answer.Value;

        var saved = await answers.SaveAsync(toStore, cancellationToken);
        if (!saved.IsValid)
        {
            return Result.WithMessages<Answer>(saved.Messages);
        }

        logger.LogInformation(
            "Answer to poll {PollId} {Action} ({Signedin}).",
            toStore.PollId,
            earlier.IsValid ? "changed" : "given",
            command.By.IsAnonymous ? "anonymous" : "signed in");

        return toStore;
    }
}
