namespace DatumPrikker.WebApp.Auth;

/// <summary>The authorization policies beyond the fallback "signed in".</summary>
public static class Policies
{
    /// <summary>Only the accounts in <c>Admin:Accounts</c> (D16).</summary>
    public const string Admin = "Beheerder";
}
