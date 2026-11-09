namespace DatumPrikker.application.Queries;

/// <summary>Every poll with its number of answers, for the admin page (D18).</summary>
public sealed record ListAllPolls
{
    /// <summary>Who is asking; only an admin gets the list.</summary>
    public Account By { get; init; } = Account.Anonymous;
}
