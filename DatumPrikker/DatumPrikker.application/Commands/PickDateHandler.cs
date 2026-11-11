namespace DatumPrikker.application.Commands;

/// <summary>Picks, changes and undoes a poll's final date, for its owner only (D22).</summary>
public sealed class PickDateHandler(IPollStore polls, ILogger<PickDateHandler> logger)
{
    private readonly IPollStore polls = polls ?? throw new ArgumentNullException(nameof(polls));
    private readonly ILogger<PickDateHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Picks a date, or changes the pick; picking closes an open poll.</summary>
    /// <param name="command">Which poll and date, and who asks.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or why it was refused.</returns>
    public Task<Result> HandleAsync(PickDate command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ChangeAsync(command.ShareToken, command.By, poll => poll.Pick(command.OptionId, command.By, Clock.UtcNow()), "picked a date for", cancellationToken);
    }

    /// <summary>Undoes the pick; the poll stays closed.</summary>
    /// <param name="command">Which poll, and who asks.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or why it was refused.</returns>
    public Task<Result> HandleAsync(UndoPick command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ChangeAsync(command.ShareToken, command.By, poll => poll.UndoPick(command.By), "undid the pick of", cancellationToken);
    }

    private async Task<Result> ChangeAsync(string shareToken, Account by, Func<Poll, Result<Poll>> change, string action, CancellationToken cancellationToken)
    {
        var poll = await polls.LoadAsync(shareToken, cancellationToken);
        if (!poll.IsValid)
        {
            return poll;
        }

        var changed = change(poll.Value);
        if (!changed.IsValid)
        {
            logger.LogWarning("Account {AccountId} was refused when it {Action} poll {PollId}.", by.Id, action, poll.Value.Id);
            return changed;
        }

        var saved = await polls.SaveAsync(changed.Value, cancellationToken);
        if (saved.IsValid)
        {
            logger.LogInformation("Account {AccountId} {Action} poll {PollId}.", by.Id, action, changed.Value.Id);
        }

        return saved;
    }
}
