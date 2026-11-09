using DatumPrikker.WebApp;
using Microsoft.AspNetCore.RateLimiting;
using Qowaiv.Diagnostics.Contracts;
using System.ComponentModel.DataAnnotations;

namespace DatumPrikker.WebApp.Pages.P;

/// <summary>Answers a poll: no account needed, a name and an email address are enough (D3).</summary>
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Answers)]
public sealed class PrikkenModel(GetPollHandler polls, GiveAnswerHandler answers) : PageModel
{
    private readonly GetPollHandler polls = polls ?? throw new ArgumentNullException(nameof(polls));
    private readonly GiveAnswerHandler answers = answers ?? throw new ArgumentNullException(nameof(answers));

    /// <summary>The poll being answered.</summary>
    public Poll Poll { get; private set; } = new Poll();

    /// <summary>Who is answering.</summary>
    public Account Account { get; private set; } = Account.Anonymous;

    /// <summary>What the respondent filled in.</summary>
    [BindProperty]
    public AnswerInput Input { get; set; } = new AnswerInput();

    /// <summary>True right after a stored answer.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Bedankt { get; set; }

    /// <summary>The sign-in link that comes back here.</summary>
    public string LoginUrl => $"/Aanmelden?ReturnUrl={Uri.EscapeDataString(Request.Path)}";

    /// <summary>Shows the poll, with name and email filled in when signed in.</summary>
    /// <param name="token">The code from the link.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The page, or 404.</returns>
    public async Task<IActionResult> OnGetAsync(string token, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(token, cancellationToken))
        {
            return NotFound();
        }

        Input = new AnswerInput { Name = Account.Name, Email = Account.Email };
        return Page();
    }

    /// <summary>Stores the answer.</summary>
    /// <param name="token">The code from the link.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The thank-you state, the form with what is wrong, or 404.</returns>
    public async Task<IActionResult> OnPostAsync(string token, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(token, cancellationToken))
        {
            return NotFound();
        }

        var given = await answers.HandleAsync(new GiveAnswer { ShareToken = token, Draft = Input.ToDraft(), By = Account }, cancellationToken);
        if (!given.IsValid)
        {
            foreach (var message in given.Messages)
            {
                ModelState.AddModelError(string.Empty, message.Message ?? string.Empty);
            }

            return Page();
        }

        return RedirectToPage(new { token, bedankt = true });
    }

    /// <summary>The choice currently selected for a date.</summary>
    /// <param name="optionId">The date option.</param>
    /// <returns>The choice, or <see cref="Availability.None"/>.</returns>
    public Availability Chosen(Guid optionId)
        => Input.Choices.TryGetValue(optionId, out var availability) ? availability : Availability.None;

    private async Task<bool> LoadAsync(string token, CancellationToken cancellationToken)
    {
        var poll = await polls.HandleAsync(new GetPoll { ShareToken = token }, cancellationToken);
        if (!poll.IsValid)
        {
            return false;
        }

        Poll = poll.Value;
        Account = AccountClaims.From(User);
        return true;
    }
}

/// <summary>The answer form.</summary>
[Mutable("Model binding fills it in.")]
public sealed class AnswerInput
{
    /// <summary>The respondent's name.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The respondent's email address.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional remark for the maker.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Comment { get; set; } = string.Empty;

    /// <summary>The choice per date option, posted as <c>Input.Choices[optionId]=Yes</c>.</summary>
    public Dictionary<Guid, Availability> Choices { get; set; } = new Dictionary<Guid, Availability>();

    /// <summary>The form as a draft for <see cref="Answer.Give"/>.</summary>
    /// <returns>The draft.</returns>
    public AnswerDraft ToDraft() => new AnswerDraft
    {
        Name = Name,
        Email = Email,
        Comment = Comment,
        Choices = [.. Choices.Select(choice => new OptionAnswer { OptionId = choice.Key, Availability = choice.Value })],
    };
}
