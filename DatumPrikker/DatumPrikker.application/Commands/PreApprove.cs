namespace DatumPrikker.application.Commands;

/// <summary>An admin pre-approves an email address (D15).</summary>
public sealed record PreApprove
{
    /// <summary>The admin.</summary>
    public Account By { get; init; } = Account.Anonymous;

    /// <summary>The address, as typed.</summary>
    public string Email { get; init; } = string.Empty;
}
