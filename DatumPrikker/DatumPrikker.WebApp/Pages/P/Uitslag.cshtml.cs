namespace DatumPrikker.WebApp.Pages.P;

/// <summary>A poll's results, the share link and the close button; for the owner (D9) and admins (D18).</summary>
public sealed class UitslagModel(GetResultsHandler results, ClosePollHandler close) : PageModel
{
    private readonly GetResultsHandler results = results ?? throw new ArgumentNullException(nameof(results));
    private readonly ClosePollHandler close = close ?? throw new ArgumentNullException(nameof(close));

    /// <summary>The tally.</summary>
    public PollResults Results { get; private set; } = new PollResults();

    /// <summary>True right after making the poll.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Nieuw { get; set; }

    /// <summary>Why the results are not shown; empty when they are.</summary>
    public string Refusal { get; private set; } = string.Empty;

    /// <summary>The link to share.</summary>
    public string ShareUrl => $"{Request.Scheme}://{Request.Host}/p/{Results.Poll.ShareToken}";

    /// <summary>True for the owner; an admin looking at somebody else's poll gets no close button.</summary>
    public bool IsOwner => Results.Poll.IsOwnedBy(AccountClaims.From(User));

    /// <summary>Shows the results.</summary>
    /// <param name="token">The code from the link.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The page, a refusal (403) or 404.</returns>
    public async Task<IActionResult> OnGetAsync(string token, CancellationToken cancellationToken)
    {
        var tallied = await results.HandleAsync(new GetResults { ShareToken = token, By = AccountClaims.From(User) }, cancellationToken);
        if (tallied.IsValid)
        {
            Results = tallied.Value;
            return Page();
        }

        if (IsNotFound(tallied))
        {
            return NotFound();
        }

        Refusal = string.Join(" ", tallied.Messages.Select(message => message.Message));
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Page();
    }

    /// <summary>Closes the poll.</summary>
    /// <param name="token">The code from the link.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page again, or 404.</returns>
    public async Task<IActionResult> OnPostSluitenAsync(string token, CancellationToken cancellationToken)
    {
        var closed = await close.HandleAsync(new ClosePoll { ShareToken = token, By = AccountClaims.From(User) }, cancellationToken);
        return IsNotFound(closed) ? NotFound() : RedirectToPage(new { token });
    }

    private static bool IsNotFound(Result result) => result.Messages.Any(message => message.Message == IPollStore.NotFound);
}
