namespace DatumPrikker.application.Commands;

/// <summary>Applies an admin's decision; anybody else is refused, whatever the page shows.</summary>
public sealed class DecideAccessHandler(IAccessStore access, Admins admins, ILogger<DecideAccessHandler> logger)
{
    private readonly IAccessStore access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly Admins admins = admins ?? throw new ArgumentNullException(nameof(admins));
    private readonly ILogger<DecideAccessHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">Who decides what about whom.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or why it was refused.</returns>
    public Task<Result> HandleAsync(DecideAccess command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return DecideAsync(command, cancellationToken);
    }

    private async Task<Result> DecideAsync(DecideAccess command, CancellationToken cancellationToken)
    {
        var allowed = admins.Require(command.By);
        if (!allowed.IsValid)
        {
            logger.LogWarning("Account {AccountId} tried to decide on access without being an admin.", command.By.Id);
            return allowed;
        }

        var current = await access.LoadAsync(command.AccountId, cancellationToken);
        if (!current.IsValid)
        {
            return current;
        }

        var decided = current.Value.Decide(command.Decision, command.By.Id, Clock.UtcNow());
        if (!decided.IsValid)
        {
            return decided;
        }

        var saved = await access.SaveAsync(decided.Value, cancellationToken);
        if (saved.IsValid)
        {
            logger.LogInformation("Admin {AdminId} set {AccountId} to {State}.", command.By.Id, command.AccountId, decided.Value.State);
        }

        return saved;
    }
}
