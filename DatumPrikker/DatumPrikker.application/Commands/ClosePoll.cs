namespace DatumPrikker.application.Commands;

/// <summary>Close a poll so it takes no more answers.</summary>
public sealed record ClosePoll
{
    /// <summary>The code from the poll's link.</summary>
    public string ShareToken { get; init; } = string.Empty;

    /// <summary>Who is closing it; only the owner may.</summary>
    public Account By { get; init; } = Account.Anonymous;
}
