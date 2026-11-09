namespace DatumPrikker.application.Commands;

/// <summary>An admin approves, declines or revokes an account's access (D14).</summary>
public sealed record DecideAccess
{
    /// <summary>The admin deciding.</summary>
    public Account By { get; init; } = Account.Anonymous;

    /// <summary>The account decided about.</summary>
    public string AccountId { get; init; } = string.Empty;

    /// <summary>The decision.</summary>
    public AccessDecision Decision { get; init; }
}
