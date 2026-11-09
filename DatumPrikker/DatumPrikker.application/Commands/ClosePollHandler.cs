namespace DatumPrikker.application.Commands;

/// <summary>Closes a poll for its owner.</summary>
public sealed class ClosePollHandler(IPollStore polls, ILogger<ClosePollHandler> logger)
{
    private readonly IPollStore polls = polls ?? throw new ArgumentNullException(nameof(polls));
    private readonly ILogger<ClosePollHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">Which poll, and who asks.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or why it was refused.</returns>
    public Task<Result> HandleAsync(ClosePoll command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return CloseAsync(command, cancellationToken);
    }

    private async Task<Result> CloseAsync(ClosePoll command, CancellationToken cancellationToken)
    {
        var poll = await polls.LoadAsync(command.ShareToken, cancellationToken);
        if (!poll.IsValid)
        {
            return poll;
        }

        var closed = poll.Value.Close(command.By, Clock.UtcNow());
        if (!closed.IsValid)
        {
            logger.LogWarning("Account {AccountId} may not close poll {PollId}.", command.By.Id, poll.Value.Id);
            return closed;
        }

        var saved = await polls.SaveAsync(closed.Value, cancellationToken);
        if (saved.IsValid)
        {
            logger.LogInformation("Poll {PollId} closed.", closed.Value.Id);
        }

        return saved;
    }
}
