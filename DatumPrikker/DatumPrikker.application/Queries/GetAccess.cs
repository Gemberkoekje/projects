namespace DatumPrikker.application.Queries;

/// <summary>Where an account stands on making polls.</summary>
public sealed record GetAccess
{
    /// <summary>The account.</summary>
    public Account Account { get; init; } = Account.Anonymous;
}
