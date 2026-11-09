namespace DatumPrikker.application.Commands;

/// <summary>Ask the admin for permission to make polls (D14).</summary>
public sealed record RequestAccess
{
    /// <summary>The signed-in account asking.</summary>
    public Account By { get; init; } = Account.Anonymous;

    /// <summary>Optional: who they are, so the admin knows the person.</summary>
    public string Note { get; init; } = string.Empty;
}
