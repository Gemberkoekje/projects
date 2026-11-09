namespace DatumPrikker.application.Queries;

/// <summary>A poll's results, for its owner.</summary>
public sealed record GetResults
{
    /// <summary>The code from the poll's link.</summary>
    public string ShareToken { get; init; } = string.Empty;

    /// <summary>Who is asking; only the owner gets the results.</summary>
    public Account By { get; init; } = Account.Anonymous;
}
