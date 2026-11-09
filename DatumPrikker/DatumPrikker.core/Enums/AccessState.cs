namespace DatumPrikker.core.Enums;

/// <summary>Where an account stands on making polls (D14).</summary>
public enum AccessState
{
    /// <summary>Never asked.</summary>
    None = 0,

    /// <summary>Aangevraagd: waiting for the admin.</summary>
    Requested = 1,

    /// <summary>Toegestaan: may make polls.</summary>
    Approved = 2,

    /// <summary>Afgewezen: the admin said no to the request.</summary>
    Declined = 3,

    /// <summary>Ingetrokken: was approved, no longer is.</summary>
    Revoked = 4,
}
