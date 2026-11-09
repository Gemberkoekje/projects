namespace DatumPrikker.application.ReadModels;

/// <summary>Where one account stands on making polls, for the Nieuwe prikker page.</summary>
public sealed record AccessStatus
{
    /// <summary>The account asking.</summary>
    public Account Account { get; init; } = Account.Anonymous;

    /// <summary>True for an admin (D16).</summary>
    public bool IsAdmin { get; init; }

    /// <summary>The account's access; an empty <see cref="Access"/> (no id) when it never asked.</summary>
    public Access Access { get; init; } = new Access();

    /// <summary>True when it asked at some point.</summary>
    public bool HasAsked => Access.Id.Length > 0;

    /// <summary>True when it may make polls: an admin, or approved.</summary>
    public bool CanCreatePolls => IsAdmin || Access.IsApproved;
}
