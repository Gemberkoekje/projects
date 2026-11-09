namespace DatumPrikker.application.Commands;

/// <summary>Make a new poll.</summary>
public sealed record CreatePoll
{
    /// <summary>What the maker filled in.</summary>
    public PollDraft Draft { get; init; } = new PollDraft();

    /// <summary>The signed-in maker.</summary>
    public Account Owner { get; init; } = Account.Anonymous;
}
