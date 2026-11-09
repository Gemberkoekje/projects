namespace DatumPrikker.application.Queries;

/// <summary>The poll behind a link, for answering it.</summary>
public sealed record GetPoll
{
    /// <summary>The code from the poll's link.</summary>
    public string ShareToken { get; init; } = string.Empty;
}
