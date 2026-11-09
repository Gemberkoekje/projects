namespace DatumPrikker.application.ReadModels;

/// <summary>One line on "Mijn afspraken": a poll you made, answered, or both.</summary>
public sealed record Appointment
{
    /// <summary>The poll.</summary>
    public Poll Poll { get; init; } = new Poll();

    /// <summary>True when you made it.</summary>
    public bool IsOwner { get; init; }

    /// <summary>Your answer; an empty <see cref="Answer"/> (no id) when you haven't answered.</summary>
    public Answer MyAnswer { get; init; } = new Answer();

    /// <summary>True when you answered it.</summary>
    public bool HasAnswered => MyAnswer.Id.Length > 0;

    /// <summary>How many people answered; the page shows it to the owner only (D9).</summary>
    public int AnswerCount { get; init; }

    /// <summary>The last thing that happened that involves you: making it, or your latest answer.</summary>
    public DateTime LastActivity => HasAnswered && MyAnswer.UpdatedOn > Poll.CreatedOn ? MyAnswer.UpdatedOn : Poll.CreatedOn;
}
