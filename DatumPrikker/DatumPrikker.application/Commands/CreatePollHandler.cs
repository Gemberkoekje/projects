namespace DatumPrikker.application.Commands;

/// <summary>
/// Makes a poll with a share token no other poll uses, for admins and approved accounts only (D14).
/// The gate lives here, not only on the page, so posting the form directly can't get round it.
/// </summary>
public sealed class CreatePollHandler(IPollStore polls, IAccessStore access, Admins admins, ILogger<CreatePollHandler> logger)
{
    /// <summary>The refusal for a signed-in account without approval.</summary>
    public const string NoAccess = "Je hebt (nog) geen toestemming om prikkers te maken.";

    private const int TokenAttempts = 5;

    private readonly IPollStore polls = polls ?? throw new ArgumentNullException(nameof(polls));
    private readonly IAccessStore access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly Admins admins = admins ?? throw new ArgumentNullException(nameof(admins));
    private readonly ILogger<CreatePollHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The new poll.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The stored poll, or the validation messages.</returns>
    public Task<Result<Poll>> HandleAsync(CreatePoll command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return CreateAsync(command, cancellationToken);
    }

    private async Task<Result<Poll>> CreateAsync(CreatePoll command, CancellationToken cancellationToken)
    {
        // Anonymous makers are refused by Poll.Create itself, with its own message.
        if (!command.Owner.IsAnonymous && !admins.Includes(command.Owner))
        {
            var granted = await access.LoadAsync(command.Owner.Id, cancellationToken);
            if (!granted.IsValid || !granted.Value.IsApproved)
            {
                logger.LogWarning("Account {AccountId} tried to make a poll without approval.", command.Owner.Id);
                return Result.WithMessages<Poll>(ValidationMessage.Error(NoAccess, nameof(command.Owner)));
            }
        }

        var token = await FreeShareTokenAsync(cancellationToken);
        var poll = Poll.Create(command.Draft, command.Owner, token, Clock.UtcNow());
        if (!poll.IsValid)
        {
            return poll;
        }

        var saved = await polls.SaveAsync(poll.Value, cancellationToken);
        if (!saved.IsValid)
        {
            return Result.WithMessages<Poll>(saved.Messages);
        }

        logger.LogInformation(
            "Poll {PollId} ({ShareToken}) made by {AccountId} with {OptionCount} dates.",
            poll.Value.Id,
            poll.Value.ShareToken,
            poll.Value.OwnerAccountId,
            poll.Value.Options.Count);

        return poll;
    }

    private async Task<string> FreeShareTokenAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < TokenAttempts; attempt++)
        {
            var token = ShareToken.New();
            if (!await polls.IsShareTokenTakenAsync(token, cancellationToken))
            {
                return token;
            }
        }

        // 10^14 tokens: five collisions in a row means the store is broken, not unlucky.
        throw new InvalidOperationException($"No free share token after {TokenAttempts} attempts.");
    }
}
