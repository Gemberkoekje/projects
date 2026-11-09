using System.Security.Cryptography;

namespace DatumPrikker.core.Models;

/// <summary>The short code in a poll's link, as in <c>/p/k7Gm2xQa</c>.</summary>
public static class ShareToken
{
    /// <summary>The token length.</summary>
    public const int Length = 8;

    // No 0/O, 1/l/I: people read these links out loud and type them over.
    private const string Alphabet = "23456789abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>A fresh random token (57^8, about 10^14 possibilities).</summary>
    /// <returns>The token.</returns>
    public static string New()
    {
        Span<char> buffer = stackalloc char[Length];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(buffer);
    }

    /// <summary>True when the text could be a token; anything else is not looked up at all.</summary>
    /// <param name="token">The text from the URL.</param>
    /// <returns>True for the right length and alphabet.</returns>
    public static bool IsWellFormed(string token)
        => token is { Length: Length } && token.All(Alphabet.Contains);
}
