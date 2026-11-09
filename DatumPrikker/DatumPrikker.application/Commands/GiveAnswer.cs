namespace DatumPrikker.application.Commands;

/// <summary>Answer a poll, or change an earlier answer given with the same email address.</summary>
public sealed record GiveAnswer
{
    /// <summary>The code from the poll's link.</summary>
    public string ShareToken { get; init; } = string.Empty;

    /// <summary>What the respondent filled in.</summary>
    public AnswerDraft Draft { get; init; } = new AnswerDraft();

    /// <summary>Who is answering; anonymous is fine.</summary>
    public Account By { get; init; } = Account.Anonymous;
}
