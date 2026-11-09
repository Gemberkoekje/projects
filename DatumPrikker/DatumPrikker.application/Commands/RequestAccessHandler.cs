namespace DatumPrikker.application.Commands;

/// <summary>
/// Stores a request for access; a pre-approved email (D15) is approved on the spot. Asking twice is
/// harmless, but after a decline or a revoke there is no asking again (D14).
/// </summary>
public sealed class RequestAccessHandler(IAccessStore access, IPreApprovalStore preApprovals, ILogger<RequestAccessHandler> logger)
{
    /// <summary>The refusal after a decline or a revoke.</summary>
    public const string AlreadyDecided = "Je aanvraag is al beoordeeld.";

    private readonly IAccessStore access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly IPreApprovalStore preApprovals = preApprovals ?? throw new ArgumentNullException(nameof(preApprovals));
    private readonly ILogger<RequestAccessHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">Who asks, and their note.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The account's access, or why it can't ask.</returns>
    public Task<Result<Access>> HandleAsync(RequestAccess command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return RequestAsync(command, cancellationToken);
    }

    private async Task<Result<Access>> RequestAsync(RequestAccess command, CancellationToken cancellationToken)
    {
        if (!command.By.IsAnonymous)
        {
            var existing = await access.LoadAsync(command.By.Id, cancellationToken);
            if (existing.IsValid)
            {
                return existing.Value.State is AccessState.Requested or AccessState.Approved
                    ? existing
                    : Result.WithMessages<Access>(ValidationMessage.Error(AlreadyDecided, nameof(command.By)));
            }
        }

        var preApproved = command.By.Email.Length > 0 && await preApprovals.IsPreApprovedAsync(command.By.Email, cancellationToken);
        var requested = Access.Request(command.By, command.Note, preApproved, Clock.UtcNow());
        if (!requested.IsValid)
        {
            return requested;
        }

        var saved = await access.SaveAsync(requested.Value, cancellationToken);
        if (!saved.IsValid)
        {
            return Result.WithMessages<Access>(saved.Messages);
        }

        logger.LogInformation(
            "Account {AccountId} ({Name}) asked to make polls: {State}.",
            requested.Value.Id,
            requested.Value.Name,
            requested.Value.State);

        return requested;
    }
}
