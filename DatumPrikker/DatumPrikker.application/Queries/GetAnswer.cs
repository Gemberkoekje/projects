namespace DatumPrikker.application.Queries;

/// <summary>An earlier answer to a poll, to fill the form with when somebody answers again.</summary>
public sealed record GetAnswer
{
    /// <summary>The poll.</summary>
    public Guid PollId { get; init; }

    /// <summary>The email address it was given with, as typed or remembered.</summary>
    public string Email { get; init; } = string.Empty;
}
