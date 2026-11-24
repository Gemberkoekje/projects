namespace Klaverjas.core.Models;

/// <summary>Checks the email addresses Dad types: a member's, and where replies go.</summary>
public static class Emailadres
{
    /// <summary>Whether a value is an email address the mail can go to.</summary>
    /// <param name="waarde">The address as typed.</param>
    /// <returns><see langword="true"/> for an address such as <c>naam@voorbeeld.nl</c>; a domain without a dot is a typo here.</returns>
    public static bool IsGeldig(string waarde)
    {
        if (string.IsNullOrWhiteSpace(waarde) || !EmailAddress.TryParse(waarde.Trim(), out var adres) || adres.IsEmptyOrUnknown())
        {
            return false;
        }

        var tekst = adres.ToString();
        return tekst[(tekst.LastIndexOf('@') + 1)..].Contains('.', StringComparison.Ordinal);
    }
}
