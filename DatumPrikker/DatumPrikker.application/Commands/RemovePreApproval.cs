namespace DatumPrikker.application.Commands;

/// <summary>An admin removes a pre-approved email address (D15).</summary>
public sealed record RemovePreApproval
{
    /// <summary>The admin.</summary>
    public Account By { get; init; } = Account.Anonymous;

    /// <summary>The address.</summary>
    public string Email { get; init; } = string.Empty;
}
