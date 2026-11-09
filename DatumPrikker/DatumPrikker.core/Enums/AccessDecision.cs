namespace DatumPrikker.core.Enums;

/// <summary>What the admin decides about an account's access (D14).</summary>
public enum AccessDecision
{
    /// <summary>No decision.</summary>
    None = 0,

    /// <summary>Toestaan: allow making polls, from any state.</summary>
    Approve = 1,

    /// <summary>Afwijzen: say no to a waiting request.</summary>
    Decline = 2,

    /// <summary>Intrekken: take making polls away from an approved account.</summary>
    Revoke = 3,
}
