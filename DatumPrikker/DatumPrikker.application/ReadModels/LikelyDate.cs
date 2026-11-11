namespace DatumPrikker.application.ReadModels;

/// <summary>
/// The most likely date, as a respondent sees it after answering (D21): the leading dates with their
/// counts, and how many answered. It carries no answers, so no names, email addresses or comments.
/// </summary>
public sealed record LikelyDate
{
    /// <summary>The dates sharing first place (most Ja, then most Misschien); empty while nobody said Ja.</summary>
    public IReadOnlyList<OptionTally> Leading { get; init; } = [];

    /// <summary>How many people answered.</summary>
    public int AnswerCount { get; init; }

    /// <summary>True when more than one date leads.</summary>
    public bool IsTie => Leading.Count > 1;
}
