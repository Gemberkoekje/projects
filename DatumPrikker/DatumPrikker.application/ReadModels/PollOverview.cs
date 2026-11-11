namespace DatumPrikker.application.ReadModels;

/// <summary>One line in the admin page's list of all polls (D18).</summary>
public sealed record PollOverview
{
    /// <summary>The poll.</summary>
    public Poll Poll { get; init; } = new Poll();

    /// <summary>True while it takes answers.</summary>
    public bool IsOpen { get; init; }

    /// <summary>How many people answered.</summary>
    public int AnswerCount { get; init; }
}
