using DatumPrikker.WebApp.Pages.Shared;

namespace DatumPrikker.WebApp.Pages.P;

/// <summary>
/// A poll's results, the share link, the close button, and picking the final date (D22); for the
/// owner (D9) and admins (D18). Closing and picking are the owner's alone.
/// </summary>
public sealed class UitslagModel(GetResultsHandler results, ClosePollHandler close, PickDateHandler pick) : PageModel
{
    private readonly GetResultsHandler results = results ?? throw new ArgumentNullException(nameof(results));
    private readonly ClosePollHandler close = close ?? throw new ArgumentNullException(nameof(close));
    private readonly PickDateHandler pick = pick ?? throw new ArgumentNullException(nameof(pick));
    private readonly DateTime now = Clock.UtcNow();

    /// <summary>The tally.</summary>
    public PollResults Results { get; private set; } = new PollResults();

    /// <summary>True right after making the poll.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Nieuw { get; set; }

    /// <summary>True right after picking a date.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Gekozen { get; set; }

    /// <summary>True right after undoing the pick.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Ongedaan { get; set; }

    /// <summary>Why the results are not shown; empty when they are.</summary>
    public string Refusal { get; private set; } = string.Empty;

    /// <summary>The link to share.</summary>
    public string ShareUrl => $"{Request.Scheme}://{Request.Host}/p/{Results.Poll.ShareToken}";

    /// <summary>True for the owner; an admin looking at somebody else's poll gets no close or pick buttons.</summary>
    public bool IsOwner => Results.Poll.IsOwnedBy(AccountClaims.From(User));

    /// <summary>True while the poll takes answers: not closed, and its deadline hasn't passed (D22).</summary>
    public bool IsOpen => Results.Poll.IsOpenAt(now);

    /// <summary>True when the deadline closed the poll, rather than its owner.</summary>
    public bool ClosedByDeadline => Results.Poll.ClosedByDeadlineAt(now);

    /// <summary>The "Dit wordt het" card; the owner also gets the undo button.</summary>
    public PickCard Pick => new PickCard { Poll = Results.Poll, ForOwner = IsOwner };

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

    /// <summary>"Dit wordt het" or "Kies deze": picks a date, which closes an open poll.</summary>
    /// <param name="token">The code from the link.</param>
    /// <param name="optie">The date option to pick.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page again, saying so when it worked; or 404.</returns>
    public async Task<IActionResult> OnPostKiesAsync(string token, Guid optie, CancellationToken cancellationToken)
    {
        var picked = await pick.HandleAsync(new PickDate { ShareToken = token, OptionId = optie, By = AccountClaims.From(User) }, cancellationToken);
        if (IsNotFound(picked))
        {
            return NotFound();
        }

        return picked.IsValid ? RedirectToPage(new { token, gekozen = true }) : RedirectToPage(new { token });
    }

    /// <summary>"Keuze ongedaan maken": the poll stays closed.</summary>
    /// <param name="token">The code from the link.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page again, saying so when it worked; or 404.</returns>
    public async Task<IActionResult> OnPostOngedaanAsync(string token, CancellationToken cancellationToken)
    {
        var undone = await pick.HandleAsync(new UndoPick { ShareToken = token, By = AccountClaims.From(User) }, cancellationToken);
        if (IsNotFound(undone))
        {
            return NotFound();
        }

        return undone.IsValid ? RedirectToPage(new { token, ongedaan = true }) : RedirectToPage(new { token });
    }

    private static bool IsNotFound(Result result) => result.Messages.Any(message => message.Message == IPollStore.NotFound);
}
