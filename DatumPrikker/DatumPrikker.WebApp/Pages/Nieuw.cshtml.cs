using Qowaiv.Diagnostics.Contracts;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace DatumPrikker.WebApp.Pages;

/// <summary>
/// Makes a new poll. Signing in is required (the fallback policy sends strangers to /Aanmelden), and
/// so is the admin's approval (D14): without it, the page offers "Toegang vragen" instead of the form.
/// </summary>
public sealed class NieuwModel(CreatePollHandler handler, GetAccessHandler access, RequestAccessHandler request) : PageModel
{
    /// <summary>How many empty date rows a fresh form has.</summary>
    public const int StartRows = 3;

    private readonly CreatePollHandler handler = handler ?? throw new ArgumentNullException(nameof(handler));
    private readonly GetAccessHandler access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly RequestAccessHandler request = request ?? throw new ArgumentNullException(nameof(request));

    /// <summary>What the maker filled in.</summary>
    [BindProperty]
    public PollInput Input { get; set; } = new PollInput();

    /// <summary>The note with an access request.</summary>
    [BindProperty]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Note { get; set; } = string.Empty;

    /// <summary>Where the signed-in account stands on making polls.</summary>
    public AccessStatus Status { get; private set; } = new AccessStatus();

    /// <summary>Shows the form, or where the account stands.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Status = await access.HandleAsync(new GetAccess { Account = AccountClaims.From(User) }, cancellationToken);
        Input = new PollInput { Options = [.. Enumerable.Range(0, StartRows).Select(_ => new OptionInput())] };
    }

    /// <summary>Asks the admin for access.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page again, showing the new state.</returns>
    public async Task<IActionResult> OnPostAanvragenAsync(CancellationToken cancellationToken)
    {
        var asked = await request.HandleAsync(new RequestAccess { By = AccountClaims.From(User), Note = Note }, cancellationToken);
        if (!asked.IsValid)
        {
            foreach (var message in asked.Messages)
            {
                ModelState.AddModelError(string.Empty, message.Message ?? string.Empty);
            }

            Status = await access.HandleAsync(new GetAccess { Account = AccountClaims.From(User) }, cancellationToken);
            return Page();
        }

        return RedirectToPage();
    }

    /// <summary>Makes the poll.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The results page of the new poll, or the form with what is wrong.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Status = await access.HandleAsync(new GetAccess { Account = AccountClaims.From(User) }, cancellationToken);

        var options = new List<DateOption>();
        for (var row = 0; row < Input.Options.Count; row++)
        {
            var input = Input.Options[row];
            if (input.IsBlank)
            {
                continue;
            }

            if (input.TryParse(out var option, out var error))
            {
                options.Add(option);
            }
            else
            {
                ModelState.AddModelError(string.Empty, $"Datum {row + 1}: {error}");
            }
        }

        if (!ModelState.IsValid)
        {
            return Redisplay();
        }

        var draft = new PollDraft
        {
            Title = Input.Title,
            Description = Input.Description,
            Location = Input.Location,
            Options = options,
        };

        var created = await handler.HandleAsync(new CreatePoll { Draft = draft, Owner = AccountClaims.From(User) }, cancellationToken);
        if (!created.IsValid)
        {
            foreach (var message in created.Messages)
            {
                ModelState.AddModelError(string.Empty, message.Message ?? string.Empty);
            }

            return Redisplay();
        }

        return RedirectToPage("/P/Uitslag", new { token = created.Value.ShareToken, nieuw = true });
    }

    private PageResult Redisplay()
    {
        if (Input.Options.Count == 0)
        {
            Input.Options.Add(new OptionInput());
        }

        return Page();
    }
}

/// <summary>The new-poll form.</summary>
[Mutable("Model binding fills it in.")]
public sealed class PollInput
{
    /// <summary>The title.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional details.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Optional place.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Location { get; set; } = string.Empty;

    /// <summary>The date rows, empty ones included.</summary>
    public List<OptionInput> Options { get; set; } = [];
}

/// <summary>One date row: a day, and optionally a time range.</summary>
[Mutable("Model binding fills it in.")]
public sealed class OptionInput
{
    /// <summary>The day as <c>yyyy-MM-dd</c> (what a date input posts).</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Date { get; set; } = string.Empty;

    /// <summary>The start as <c>HH:mm</c>; empty for the whole day.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Start { get; set; } = string.Empty;

    /// <summary>The end as <c>HH:mm</c>; empty for the whole day.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string End { get; set; } = string.Empty;

    /// <summary>True for a row left empty, which is skipped.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Date) && string.IsNullOrWhiteSpace(Start) && string.IsNullOrWhiteSpace(End);

    /// <summary>Reads the row.</summary>
    /// <param name="option">The date option.</param>
    /// <param name="error">What is wrong, in Dutch.</param>
    /// <returns>True when the row makes sense.</returns>
    public bool TryParse(out DateOption option, out string error)
    {
        option = new DateOption();
        error = string.Empty;

        if (!DateOnly.TryParseExact(Date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            error = "kies een dag.";
            return false;
        }

        var hasStart = !string.IsNullOrWhiteSpace(Start);
        var hasEnd = !string.IsNullOrWhiteSpace(End);
        if (!hasStart && !hasEnd)
        {
            option = new DateOption { Date = date };
            return true;
        }

        if (hasStart != hasEnd)
        {
            error = "vul een begin- én eindtijd in, of laat beide leeg voor de hele dag.";
            return false;
        }

        if (!TimeOnly.TryParse(Start.Trim(), CultureInfo.InvariantCulture, out var start)
            || !TimeOnly.TryParse(End.Trim(), CultureInfo.InvariantCulture, out var end))
        {
            error = "die tijd klopt niet.";
            return false;
        }

        option = new DateOption { Date = date, HasTime = true, Start = start, End = end };
        return true;
    }
}
