using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace DatumPrikker.WebApp.Auth;

/// <summary>The name and email this browser last answered with anonymously.</summary>
public sealed record Remembered
{
    /// <summary>Nothing remembered.</summary>
    public static readonly Remembered Nobody = new Remembered();

    /// <summary>The name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The normalised email address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>True when nothing is remembered.</summary>
    public bool IsNobody => Email.Length == 0;
}

/// <summary>
/// Remembers an anonymous respondent in a protected cookie, so their next visit shows their earlier
/// answer. Protected, not just encoded: otherwise anybody could put a neighbour's address in the
/// cookie and read the neighbour's answer.
/// </summary>
public sealed class RespondentMemory(IDataProtectionProvider provider)
{
    /// <summary>The cookie's name.</summary>
    public const string CookieName = "datumprikker-ik";

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    private readonly IDataProtector protector = (provider ?? throw new ArgumentNullException(nameof(provider)))
        .CreateProtector("DatumPrikker.RespondentMemory.v1");

    /// <summary>Reads the cookie; a missing, tampered or stale one remembers nobody.</summary>
    /// <param name="request">The request.</param>
    /// <returns>Who this browser answered as.</returns>
    public Remembered Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return Remembered.Nobody;
        }

        try
        {
            // Email first: it can't contain a line break, the name might.
            var parts = protector.Unprotect(value).Split('\n', 2);
            return parts.Length == 2 && Email.TryNormalise(parts[0], out var email)
                ? new Remembered { Email = email, Name = parts[1] }
                : Remembered.Nobody;
        }
        catch (CryptographicException)
        {
            return Remembered.Nobody;
        }
    }

    /// <summary>Remembers who just answered.</summary>
    /// <param name="response">The response to set the cookie on.</param>
    /// <param name="name">The name they gave.</param>
    /// <param name="email">The normalised email address they gave.</param>
    public void Write(HttpResponse response, string name, string email)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Append(CookieName, protector.Protect($"{email}\n{name}"), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,

            // Production is https behind the ingress; browsers also accept Secure cookies on http://localhost.
            Secure = true,
            MaxAge = Lifetime,
        });
    }

    /// <summary>Forgets this browser's respondent ("Niet jij?").</summary>
    /// <param name="response">The response to clear the cookie on.</param>
    public static void Forget(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(CookieName);
    }
}
