namespace DatumPrikker.core.Models;

/// <summary>A poll: a title and the dates it proposes. Its answers are separate <see cref="Answer"/> documents.</summary>
public sealed record Poll
{
    /// <summary>The most date options one poll may propose.</summary>
    public const int MaxOptions = 30;

    /// <summary>The longest title.</summary>
    public const int MaxTitleLength = 120;

    /// <summary>The longest location.</summary>
    public const int MaxLocationLength = 200;

    /// <summary>The longest description.</summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>The longest maker's name.</summary>
    public const int MaxOwnerNameLength = 80;

    /// <summary>The document id.</summary>
    public Guid Id { get; init; }

    /// <summary>The code in the poll's link.</summary>
    public string ShareToken { get; init; } = string.Empty;

    /// <summary>What it is about, such as "Etentje met de buren".</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional details.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Optional place.</summary>
    public string Location { get; init; } = string.Empty;

    /// <summary>The account that made it (see <see cref="Account.Id"/>).</summary>
    public string OwnerAccountId { get; init; } = string.Empty;

    /// <summary>The maker's name, as shown to respondents.</summary>
    public string OwnerName { get; init; } = string.Empty;

    /// <summary>The proposed dates, in date and time order.</summary>
    public IReadOnlyList<DateOption> Options { get; init; } = [];

    /// <summary>When it was made (UTC).</summary>
    public DateTime CreatedOn { get; init; }

    /// <summary>When the owner closed it (UTC); <see langword="default"/> while open.</summary>
    public DateTime ClosedOn { get; init; }

    /// <summary>True while it takes answers.</summary>
    public bool IsOpen => ClosedOn == default;

    /// <summary>True when the account made this poll.</summary>
    /// <param name="account">Who is asking.</param>
    /// <returns>True for the owner.</returns>
    public bool IsOwnedBy(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return !account.IsAnonymous && string.Equals(OwnerAccountId, account.Id, StringComparison.Ordinal);
    }

    /// <summary>Makes a new poll.</summary>
    /// <param name="draft">What the maker filled in.</param>
    /// <param name="owner">The signed-in maker.</param>
    /// <param name="shareToken">A token no other poll uses.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>The poll, or why it can't be made.</returns>
    public static Result<Poll> Create(PollDraft draft, Account owner, string shareToken, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(owner);

        var errors = new List<IValidationMessage>();
        var title = draft.Title.Trim();
        var location = draft.Location.Trim();
        var description = draft.Description.Trim();

        // The maker chooses how respondents see them; left empty, the account's name stands in.
        var ownerName = draft.OwnerName.Trim();
        if (ownerName.Length == 0)
        {
            ownerName = owner.Name.Trim();
        }

        if (owner.IsAnonymous)
        {
            errors.Add(ValidationMessage.Error("Log in om een prikker te maken.", nameof(owner)));
        }

        if (title.Length == 0)
        {
            errors.Add(ValidationMessage.Error("Geef je prikker een titel.", nameof(draft.Title)));
        }
        else if (title.Length > MaxTitleLength)
        {
            errors.Add(ValidationMessage.Error($"De titel mag hooguit {MaxTitleLength} tekens lang zijn.", nameof(draft.Title)));
        }

        if (ownerName.Length > MaxOwnerNameLength)
        {
            errors.Add(ValidationMessage.Error($"Je naam mag hooguit {MaxOwnerNameLength} tekens lang zijn.", nameof(draft.OwnerName)));
        }

        if (location.Length > MaxLocationLength)
        {
            errors.Add(ValidationMessage.Error($"De locatie mag hooguit {MaxLocationLength} tekens lang zijn.", nameof(draft.Location)));
        }

        if (description.Length > MaxDescriptionLength)
        {
            errors.Add(ValidationMessage.Error($"De beschrijving mag hooguit {MaxDescriptionLength} tekens lang zijn.", nameof(draft.Description)));
        }

        errors.AddRange(OptionErrors(draft.Options));

        if (errors.Count > 0)
        {
            return Result.WithMessages<Poll>(errors);
        }

        return new Poll
        {
            Id = Guid.NewGuid(),
            ShareToken = shareToken,
            Title = title,
            Description = description,
            Location = location,
            OwnerAccountId = owner.Id,
            OwnerName = ownerName,
            Options = [.. draft.Options
                .OrderBy(option => option.Date)
                .ThenBy(option => option.HasTime)
                .ThenBy(option => option.Start)
                .Select(option => option with { Id = Guid.NewGuid() })],
            CreatedOn = now,
        };
    }

    /// <summary>Closes the poll; it takes no more answers.</summary>
    /// <param name="by">Who is closing it; only the owner may.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>The closed poll, or why it can't be closed.</returns>
    public Result<Poll> Close(Account by, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(by);

        if (!IsOwnedBy(by))
        {
            return Result.WithMessages<Poll>(ValidationMessage.Error("Alleen de maker kan deze prikker sluiten.", nameof(by)));
        }

        return IsOpen ? this with { ClosedOn = now } : this;
    }

    private static IEnumerable<IValidationMessage> OptionErrors(IReadOnlyList<DateOption> options)
    {
        if (options.Count == 0)
        {
            yield return ValidationMessage.Error("Stel minstens één datum voor.", nameof(PollDraft.Options));
            yield break;
        }

        if (options.Count > MaxOptions)
        {
            yield return ValidationMessage.Error($"Stel hooguit {MaxOptions} datums voor.", nameof(PollDraft.Options));
        }

        if (options.Any(option => option.Date == default))
        {
            yield return ValidationMessage.Error("Elke datum heeft een dag nodig.", nameof(PollDraft.Options));
        }

        if (options.Any(option => option.HasTime && option.End <= option.Start))
        {
            yield return ValidationMessage.Error("Een eindtijd moet na de begintijd liggen.", nameof(PollDraft.Options));
        }

        var duplicates = options.Where((option, index) => options.Take(index).Any(option.SameSlotAs));
        if (duplicates.Any())
        {
            yield return ValidationMessage.Error("Je hebt dezelfde datum twee keer voorgesteld.", nameof(PollDraft.Options));
        }
    }
}
