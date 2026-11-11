using DatumPrikker.WebApp;
using Microsoft.AspNetCore.RateLimiting;
using Qowaiv.Diagnostics.Contracts;
using System.ComponentModel.DataAnnotations;

namespace DatumPrikker.WebApp.Pages.P;

/// <summary>
/// Answers a poll: no account needed, a name and an email address are enough (D3). Signed in, or
/// remembered from an earlier anonymous answer, the form starts from your earlier answer, and the
/// page shows which date is leading (D21). It never holds anybody else's answers.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Answers)]
public sealed class PrikkenModel(GetPollHandler polls, GetAnswerHandler earlierAnswers, GetLikelyDateHandler likelyDates, GiveAnswerHandler answers, RespondentMemory memory) : PageModel
{
    private readonly GetPollHandler polls = polls ?? throw new ArgumentNullException(nameof(polls));
    private readonly GetAnswerHandler earlierAnswers = earlierAnswers ?? throw new ArgumentNullException(nameof(earlierAnswers));
    private readonly GetLikelyDateHandler likelyDates = likelyDates ?? throw new ArgumentNullException(nameof(likelyDates));
    private readonly GiveAnswerHandler answers = answers ?? throw new ArgumentNullException(nameof(answers));
    private readonly RespondentMemory memory = memory ?? throw new ArgumentNullException(nameof(memory));

    /// <summary>The poll being answered.</summary>
    public Poll Poll { get; private set; } = new Poll();

    /// <summary>Who is answering.</summary>
    public Account Account { get; private set; } = Account.Anonymous;

    /// <summary>Who this browser answered as before, when anonymous.</summary>
    public Remembered Remembered { get; private set; } = Remembered.Nobody;

    /// <summary>The earlier answer the form starts from; an empty <see cref="Answer"/> when there is none.</summary>
    public Answer Earlier { get; private set; } = new Answer();

    /// <summary>True when the form starts from an earlier answer.</summary>
    public bool HasEarlier => Earlier.Id.Length > 0;

    /// <summary>The leading dates and their counts (D21); only filled in when <see cref="ShowsLikely"/>.</summary>
    public LikelyDate Likely { get; private set; } = new LikelyDate();

    /// <summary>True when the "Zo te zien wordt het…" card shows: only once this respondent answered (D21).</summary>
    public bool ShowsLikely { get; private set; }

    /// <summary>What the respondent filled in.</summary>
    [BindProperty]
    public AnswerInput Input { get; set; } = new AnswerInput();

    /// <summary>True right after a stored answer.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Bedankt { get; set; }

    /// <summary>The sign-in link that comes back here.</summary>
    public string LoginUrl => $"/Aanmelden?ReturnUrl={Uri.EscapeDataString(Request.Path)}";

    /// <summary>The sign-in link that ends on "Mijn afspraken".</summary>
    public static string LoginToAppointmentsUrl => $"/Aanmelden?ReturnUrl={Uri.EscapeDataString("/Afspraken")}";

    /// <summary>Shows the poll, starting from the earlier answer when there is one.</summary>
    /// <param name="token">The code from the link.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The page, or 404.</returns>
    public async Task<IActionResult> OnGetAsync(string token, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(token, cancellationToken))
        {
            return NotFound();
        }

        var email = Account.Email.Length > 0 ? Account.Email : Remembered.Email;
        if (email.Length > 0)
        {
            var earlier = await earlierAnswers.HandleAsync(new GetAnswer { PollId = Poll.Id, Email = email }, cancellationToken);
            Earlier = earlier.IsValid ? earlier.Value : new Answer();
        }

        if (HasEarlier)
        {
            Input = AnswerInput.From(Earlier, Account);

            var likely = await likelyDates.HandleAsync(new GetLikelyDate { ShareToken = token, Email = Earlier.Email }, cancellationToken);
            ShowsLikely = likely.IsValid;
            Likely = likely.IsValid ? likely.Value : new LikelyDate();
        }
        else
        {
            Input = new AnswerInput { Name = Account.IsAnonymous ? Remembered.Name : Account.Name, Email = email };
        }

        return Page();
    }

    /// <summary>Stores the answer, and remembers an anonymous respondent for next time.</summary>
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

        if (Account.IsAnonymous)
        {
            memory.Write(Response, given.Value.Name, given.Value.Email);
        }

        return RedirectToPage(new { token, bedankt = true });
    }

    /// <summary>"Niet jij?": forgets who this browser answered as, and shows an empty form.</summary>
    /// <param name="token">The code from the link.</param>
    /// <returns>This page again.</returns>
    public IActionResult OnPostVergeten(string token)
    {
        RespondentMemory.Forget(Response);
        return RedirectToPage(new { token });
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
        Remembered = Account.IsAnonymous ? memory.Read(Request) : Remembered.Nobody;
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

    /// <summary>The form filled from an earlier answer. Signed in, the email is the account's (D3).</summary>
    /// <param name="earlier">The earlier answer.</param>
    /// <param name="account">Who is answering now.</param>
    /// <returns>The form.</returns>
    public static AnswerInput From(Answer earlier, Account account)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(account);

        return new AnswerInput
        {
            Name = earlier.Name,
            Email = account.Email.Length > 0 ? account.Email : earlier.Email,
            Comment = earlier.Comment,
            Choices = earlier.Choices.ToDictionary(choice => choice.OptionId, choice => choice.Availability),
        };
    }

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
