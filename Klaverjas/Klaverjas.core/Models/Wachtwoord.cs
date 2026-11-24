using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Klaverjas.core.Models;

/// <summary>
/// Dad's own password, kept as a salted PBKDF2 hash (D7). The players' password is not hashed: Dad
/// must see it to print it on the slips (D5).
/// </summary>
public static class Wachtwoord
{
    /// <summary>The shortest password Dad may choose for himself.</summary>
    public const int MinimumLengteBeheer = 6;

    /// <summary>The shortest players' password.</summary>
    public const int MinimumLengteSpelers = 4;

    private const string Soort = "pbkdf2-sha256";
    private const int Rondes = 600_000;
    private const int ZoutLengte = 16;
    private const int HashLengte = 32;

    /// <summary>Hashes a new password.</summary>
    /// <param name="wachtwoord">The password as typed.</param>
    /// <returns>The kind, the rounds, the salt and the hash in one string.</returns>
    public static string Maak(string wachtwoord)
    {
        ArgumentNullException.ThrowIfNull(wachtwoord);

        var zout = RandomNumberGenerator.GetBytes(ZoutLengte);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(wachtwoord), zout, Rondes, HashAlgorithmName.SHA256, HashLengte);
        return string.Join('$', Soort, Rondes.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(zout), Convert.ToBase64String(hash));
    }

    /// <summary>Checks a typed password against a stored hash, in constant time.</summary>
    /// <param name="gegeven">What was typed.</param>
    /// <param name="opgeslagen">The stored hash from <see cref="Maak"/>.</param>
    /// <returns><see langword="true"/> when they match; <see langword="false"/> for any other stored value.</returns>
    public static bool Klopt(string gegeven, string opgeslagen)
    {
        var delen = (opgeslagen ?? string.Empty).Split('$');

        if (delen.Length != 4 || delen[0] != Soort || !int.TryParse(delen[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rondes) || rondes < 1)
        {
            return false;
        }

        try
        {
            var zout = Convert.FromBase64String(delen[2]);
            var verwacht = Convert.FromBase64String(delen[3]);
            var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(gegeven ?? string.Empty), zout, rondes, HashAlgorithmName.SHA256, verwacht.Length);
            return CryptographicOperations.FixedTimeEquals(hash, verwacht);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// The players' password as compared: trimmed and without capitals. Phones capitalise the first
    /// letter and older players do not see it; the password only opens a read-only page.
    /// </summary>
    /// <param name="wachtwoord">The password as typed or stored.</param>
    /// <returns>The form both sides are compared in.</returns>
    public static string Spelersvorm(string wachtwoord) => (wachtwoord ?? string.Empty).Trim().ToLowerInvariant();
}
