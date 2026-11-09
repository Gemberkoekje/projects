using System.ComponentModel.DataAnnotations;

namespace DatumPrikker.WebApp.Pages;

/// <summary>
/// The admin page (D18): waiting requests, who has access, pre-approved addresses and every poll.
/// The policy keeps non-admins out; every handler behind it checks again.
/// </summary>
[Authorize(Policy = Policies.Admin)]
public sealed class BeheerModel(
    ListAccessHandler accessList,
    ListAllPollsHandler pollList,
    DecideAccessHandler decide,
    PreApprovalHandler preApprovals) : PageModel
{
    private readonly ListAccessHandler accessList = accessList ?? throw new ArgumentNullException(nameof(accessList));
    private readonly ListAllPollsHandler pollList = pollList ?? throw new ArgumentNullException(nameof(pollList));
    private readonly DecideAccessHandler decide = decide ?? throw new ArgumentNullException(nameof(decide));
    private readonly PreApprovalHandler preApprovals = preApprovals ?? throw new ArgumentNullException(nameof(preApprovals));

    /// <summary>Everybody's access.</summary>
    public AccessOverview Overview { get; private set; } = new AccessOverview();

    /// <summary>Every poll, newest first.</summary>
    public IReadOnlyList<PollOverview> Polls { get; private set; } = [];

    /// <summary>The admin looking.</summary>
    public Account Account { get; private set; } = Account.Anonymous;

    /// <summary>An address to pre-approve.</summary>
    [BindProperty]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Shows the page.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    /// <summary>Approves an account.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page again.</returns>
    public Task<IActionResult> OnPostToestaanAsync(string accountId, CancellationToken cancellationToken)
        => DecideAsync(accountId, AccessDecision.Approve, cancellationToken);

    /// <summary>Declines a waiting request.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page again.</returns>
    public Task<IActionResult> OnPostAfwijzenAsync(string accountId, CancellationToken cancellationToken)
        => DecideAsync(accountId, AccessDecision.Decline, cancellationToken);

    /// <summary>Revokes an approval; the account's polls keep working.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page again.</returns>
    public Task<IActionResult> OnPostIntrekkenAsync(string accountId, CancellationToken cancellationToken)
        => DecideAsync(accountId, AccessDecision.Revoke, cancellationToken);

    /// <summary>Pre-approves <see cref="Email"/>.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page again.</returns>
    public async Task<IActionResult> OnPostVoorafAsync(CancellationToken cancellationToken)
        => await ShowAsync(await preApprovals.HandleAsync(new PreApprove { By = AccountClaims.From(User), Email = Email }, cancellationToken), cancellationToken);

    /// <summary>Removes a pre-approved address.</summary>
    /// <param name="email">The address.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page again.</returns>
    public async Task<IActionResult> OnPostVerwijderVoorafAsync(string email, CancellationToken cancellationToken)
        => await ShowAsync(await preApprovals.HandleAsync(new RemovePreApproval { By = AccountClaims.From(User), Email = email }, cancellationToken), cancellationToken);

    private async Task<IActionResult> DecideAsync(string accountId, AccessDecision decision, CancellationToken cancellationToken)
        => await ShowAsync(await decide.HandleAsync(new DecideAccess { By = AccountClaims.From(User), AccountId = accountId, Decision = decision }, cancellationToken), cancellationToken);

    private async Task<IActionResult> ShowAsync(Result result, CancellationToken cancellationToken)
    {
        if (result.IsValid)
        {
            return RedirectToPage();
        }

        foreach (var message in result.Messages)
        {
            ModelState.AddModelError(string.Empty, message.Message ?? string.Empty);
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Account = AccountClaims.From(User);

        var overview = await accessList.HandleAsync(new ListAccess { By = Account }, cancellationToken);
        Overview = overview.IsValid ? overview.Value : new AccessOverview();

        var polls = await pollList.HandleAsync(new ListAllPolls { By = Account }, cancellationToken);
        Polls = polls.IsValid ? polls.Value : [];
    }
}
