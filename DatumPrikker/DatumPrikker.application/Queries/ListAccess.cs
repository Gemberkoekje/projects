namespace DatumPrikker.application.Queries;

/// <summary>Everybody's access and the pre-approved addresses, for the admin page.</summary>
public sealed record ListAccess
{
    /// <summary>Who is asking; only an admin gets the list.</summary>
    public Account By { get; init; } = Account.Anonymous;
}
