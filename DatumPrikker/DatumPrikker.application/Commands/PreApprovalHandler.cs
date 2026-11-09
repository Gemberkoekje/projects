namespace DatumPrikker.application.Commands;

/// <summary>Adds and removes pre-approved addresses, for admins only.</summary>
public sealed class PreApprovalHandler(IPreApprovalStore preApprovals, Admins admins, ILogger<PreApprovalHandler> logger)
{
    private readonly IPreApprovalStore preApprovals = preApprovals ?? throw new ArgumentNullException(nameof(preApprovals));
    private readonly Admins admins = admins ?? throw new ArgumentNullException(nameof(admins));
    private readonly ILogger<PreApprovalHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Pre-approves an address.</summary>
    /// <param name="command">Who adds which address.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or why it was refused.</returns>
    public Task<Result> HandleAsync(PreApprove command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return AddAsync(command, cancellationToken);
    }

    /// <summary>Removes a pre-approved address.</summary>
    /// <param name="command">Who removes which address.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or why it was refused.</returns>
    public Task<Result> HandleAsync(RemovePreApproval command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return RemoveAsync(command, cancellationToken);
    }

    private async Task<Result> AddAsync(PreApprove command, CancellationToken cancellationToken)
    {
        var allowed = admins.Require(command.By);
        if (!allowed.IsValid)
        {
            return allowed;
        }

        var preApproval = PreApproval.For(command.Email, command.By.Id, Clock.UtcNow());
        if (!preApproval.IsValid)
        {
            return preApproval;
        }

        var saved = await preApprovals.SaveAsync(preApproval.Value, cancellationToken);
        if (saved.IsValid)
        {
            logger.LogInformation("Admin {AdminId} pre-approved an email address.", command.By.Id);
        }

        return saved;
    }

    private async Task<Result> RemoveAsync(RemovePreApproval command, CancellationToken cancellationToken)
    {
        var allowed = admins.Require(command.By);
        if (!allowed.IsValid)
        {
            return allowed;
        }

        return Email.TryNormalise(command.Email, out var email)
            ? await preApprovals.DeleteAsync(email, cancellationToken)
            : Result.OK;
    }
}
