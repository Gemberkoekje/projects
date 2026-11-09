namespace DatumPrikker.core.Models;

/// <summary>Email addresses as DatumPrikker compares them: trimmed and lower case.</summary>
public static class Email
{
    /// <summary>Normalises an email address.</summary>
    /// <param name="input">What somebody typed, or what a provider gave.</param>
    /// <param name="normalised">The address in lower case; empty when it is not a valid address.</param>
    /// <returns>True when the input is a valid address.</returns>
    public static bool TryNormalise(string input, out string normalised)
    {
        if (!string.IsNullOrWhiteSpace(input)
            && EmailAddress.TryParse(input.Trim(), out var address)
            && !address.IsEmptyOrUnknown())
        {
            normalised = address.ToString().ToLowerInvariant();
            return true;
        }

        normalised = string.Empty;
        return false;
    }
}
