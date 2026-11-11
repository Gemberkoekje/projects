namespace DatumPrikker.core.Models;

/// <summary>One respondent's answer to one poll. Its id is the poll plus the email, so answering again replaces it.</summary>
public sealed record Answer
{
    /// <summary>The longest name.</summary>
    public const int MaxNameLength = 80;

    /// <summary>The longest comment.</summary>
    public const int MaxCommentLength = 1000;

    /// <summary>The refusal when somebody anonymous tries to replace a claimed answer (D4).</summary>
    public const string ClaimedMessage = "Dit antwoord is gegeven door iemand die was ingelogd. Log in om het te wijzigen.";

    /// <summary>The document id: see <see cref="IdFor"/>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The poll it answers.</summary>
    public Guid PollId { get; init; }

    /// <summary>The respondent's name, shown in the results.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The respondent's normalised email address; only the poll's owner sees it.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>The account it was given from; empty when given anonymously.</summary>
    public string AccountId { get; init; } = string.Empty;

    /// <summary>One choice per date option the respondent answered.</summary>
    public IReadOnlyList<OptionAnswer> Choices { get; init; } = [];

    /// <summary>Optional remark for the maker.</summary>
    public string Comment { get; init; } = string.Empty;

    /// <summary>When it was first given (UTC).</summary>
    public DateTime CreatedOn { get; init; }

    /// <summary>When it was last changed (UTC).</summary>
    public DateTime UpdatedOn { get; init; }

    /// <summary>True when it was given while signed in; then only a signed-in respondent may replace it (D4).</summary>
    public bool IsClaimed => AccountId.Length > 0;

    /// <summary>Whether this (earlier) answer may be replaced by a new one from <paramref name="by"/>.</summary>
    /// <param name="by">Who is answering now. Signed in, they can only answer as their own address, so any account may.</param>
    /// <returns>OK, or <see cref="ClaimedMessage"/> for somebody anonymous replacing a claimed answer.</returns>
    public Result ReplaceableBy(Account by)
    {
        ArgumentNullException.ThrowIfNull(by);

        return IsClaimed && by.IsAnonymous
            ? Result.WithMessages(ValidationMessage.Error(ClaimedMessage, nameof(by)))
            : Result.OK;
    }

    /// <summary>The respondent's choice for one date option.</summary>
    /// <param name="optionId">The option.</param>
    /// <returns>The choice, or <see cref="Availability.None"/>.</returns>
    public Availability ChoiceFor(Guid optionId)
        => Choices.FirstOrDefault(choice => choice.OptionId == optionId)?.Availability ?? Availability.None;

    /// <summary>The document id for a poll and a normalised email.</summary>
    /// <param name="pollId">The poll.</param>
    /// <param name="email">The normalised email address.</param>
    /// <returns>The id.</returns>
    public static string IdFor(Guid pollId, string email) => $"{pollId:N}:{email}";

    /// <summary>Gives (or replaces) an answer.</summary>
    /// <param name="poll">The poll being answered.</param>
    /// <param name="draft">What the respondent filled in.</param>
    /// <param name="by">Who is answering; anonymous is fine.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>The answer, or why it can't be given.</returns>
    public static Result<Answer> Give(Poll poll, AnswerDraft draft, Account by, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(poll);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(by);

        if (!poll.IsOpenAt(now))
        {
            return Result.WithMessages<Answer>(ValidationMessage.Error("Deze prikker is gesloten en neemt geen antwoorden meer aan.", nameof(poll)));
        }

        var errors = new List<IValidationMessage>();
        var name = draft.Name.Trim();
        var comment = draft.Comment.Trim();

        if (name.Length == 0)
        {
            errors.Add(ValidationMessage.Error("Vul je naam in.", nameof(draft.Name)));
        }
        else if (name.Length > MaxNameLength)
        {
            errors.Add(ValidationMessage.Error($"Je naam mag hooguit {MaxNameLength} tekens lang zijn.", nameof(draft.Name)));
        }

        // Signed in, you answer as your account's address: you can't type somebody else's.
        var typed = by.Email.Length > 0 ? by.Email : draft.Email;
        if (!Models.Email.TryNormalise(typed, out var email))
        {
            errors.Add(ValidationMessage.Error("Vul een geldig e-mailadres in.", nameof(draft.Email)));
        }

        if (comment.Length > MaxCommentLength)
        {
            errors.Add(ValidationMessage.Error($"Je opmerking mag hooguit {MaxCommentLength} tekens lang zijn.", nameof(draft.Comment)));
        }

        var optionIds = poll.Options.Select(option => option.Id).ToHashSet();
        if (draft.Choices.Any(choice => !optionIds.Contains(choice.OptionId)))
        {
            errors.Add(ValidationMessage.Error("Je antwoord noemt een datum die niet bij deze prikker hoort.", nameof(draft.Choices)));
        }

        var choices = poll.Options
            .Select(option => new OptionAnswer
            {
                OptionId = option.Id,
                Availability = draft.Choices.LastOrDefault(choice => choice.OptionId == option.Id)?.Availability ?? Availability.None,
            })
            .Where(choice => choice.Availability != Availability.None)
            .ToArray();

        if (choices.Length == 0)
        {
            errors.Add(ValidationMessage.Error("Kies bij minstens één datum Ja, Misschien of Nee.", nameof(draft.Choices)));
        }

        if (errors.Count > 0)
        {
            return Result.WithMessages<Answer>(errors);
        }

        return new Answer
        {
            Id = IdFor(poll.Id, email),
            PollId = poll.Id,
            Name = name,
            Email = email,
            AccountId = by.Id,
            Choices = choices,
            Comment = comment,
            CreatedOn = now,
            UpdatedOn = now,
        };
    }
}
