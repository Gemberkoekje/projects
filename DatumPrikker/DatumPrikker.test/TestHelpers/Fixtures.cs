namespace DatumPrikker.test.TestHelpers;

/// <summary>Accounts, drafts and polls the specs share.</summary>
internal static class Fixtures
{
    public static readonly DateTime Now = new DateTime(2026, 11, 09, 12, 00, 00, DateTimeKind.Utc);

    public static readonly Account Owner = new Account { Id = "google:111", Name = "Arie", Email = "arie@example.com" };

    public static readonly Account Stranger = new Account { Id = "microsoft:222", Name = "Bea", Email = "bea@example.com" };

    public static readonly Account Admin = new Account { Id = "google:999", Name = "Beheerder", Email = "beheer@example.com" };

    /// <summary>Only <see cref="Admin"/> is an admin.</summary>
    public static readonly Admins Admins = Admins.Parse(Admin.Id);

    public static DateOption WholeDay(int day) => new DateOption { Date = new DateOnly(2026, 12, day) };

    public static DateOption Evening(int day) => new DateOption
    {
        Date = new DateOnly(2026, 12, day),
        HasTime = true,
        Start = new TimeOnly(19, 0),
        End = new TimeOnly(22, 0),
    };

    public static PollDraft Draft(params DateOption[] options) => new PollDraft
    {
        Title = "Etentje met de buren",
        Location = "Bij ons",
        Options = options.Length == 0 ? [Evening(12), Evening(13), WholeDay(14)] : options,
    };

    public static Poll Poll(params DateOption[] options)
    {
        var poll = core.Models.Poll.Create(Draft(options), Owner, "k7Gm2xQa", Now);
        poll.IsValid.Should().BeTrue();
        return poll.Value;
    }

    public static AnswerDraft AnswerFrom(string name, string email, Poll poll, params Availability[] choices) => new AnswerDraft
    {
        Name = name,
        Email = email,
        Choices = [.. poll.Options.Zip(choices, (option, choice) => new OptionAnswer { OptionId = option.Id, Availability = choice })],
    };

    public static Answer Answer(string name, string email, Poll poll, params Availability[] choices)
    {
        var answer = core.Models.Answer.Give(poll, AnswerFrom(name, email, poll, choices), Account.Anonymous, Now);
        answer.IsValid.Should().BeTrue();
        return answer.Value;
    }

    public static IEnumerable<string> Errors(this Result result) => result.Messages.Select(message => message.Message ?? string.Empty);
}
