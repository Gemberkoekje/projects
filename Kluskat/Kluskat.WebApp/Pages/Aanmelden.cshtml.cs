using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Kluskat.WebApp.Pages;

/// <summary>
/// The one password. Typed once per device; the cookie then lasts a year and slides, so the klus
/// screen is never more than one tap away.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicy)]
public sealed class AanmeldenModel(IConfiguration configuration, ILogger<AanmeldenModel> logger) : PageModel
{
    /// <summary>The rate-limit policy on this page.</summary>
    internal const string RateLimitPolicy = "aanmelden";

    /// <summary>How long a sign-in lasts without use.</summary>
    internal static TimeSpan Geldigheid { get; } = TimeSpan.FromDays(365);

    /// <summary>The password as typed.</summary>
    [BindProperty]
    public string Wachtwoord { get; set; } = string.Empty;

    /// <summary>Why the last attempt failed, if it did.</summary>
    public string Fout { get; private set; } = string.Empty;

    /// <summary>Shows the form, or skips it when already signed in.</summary>
    /// <returns>The page, or the klus screen.</returns>
    public IActionResult OnGet()
        => User.Identity is { IsAuthenticated: true } ? LocalRedirect("~/") : Page();

    /// <summary>Checks the password and signs in.</summary>
    /// <param name="returnUrl">Where to go afterwards; only local addresses are honoured.</param>
    /// <returns>The return address, or the form with an error.</returns>
    public async Task<IActionResult> OnPostAsync(string returnUrl = "~/")
    {
        var verwacht = configuration["Auth:Password"];

        if (string.IsNullOrWhiteSpace(verwacht))
        {
            throw new InvalidOperationException("Auth:Password is not set.");
        }

        if (!Klopt(Wachtwoord, verwacht))
        {
            logger.LogWarning("Aanmelden mislukt vanaf {Adres}.", HttpContext.Connection.RemoteIpAddress);
            Fout = "Dat wachtwoord klopt niet.";
            return Page();
        }

        var identiteit = new ClaimsIdentity([new Claim(ClaimTypes.Name, "kluskat")], CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identiteit),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });

        logger.LogInformation("Aangemeld vanaf {Adres}.", HttpContext.Connection.RemoteIpAddress);
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
    }

    /// <summary>Signs out on this device.</summary>
    /// <returns>The sign-in form.</returns>
    public async Task<IActionResult> OnPostAfmeldenAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage();
    }

    /// <summary>Compares two passwords in constant time.</summary>
    /// <param name="gegeven">What was typed.</param>
    /// <param name="verwacht">The configured password.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    internal static bool Klopt(string gegeven, string verwacht)
    {
        // Hashing first makes both sides the same length, so the comparison time says nothing
        // about the length of the real password either.
        var links = SHA256.HashData(Encoding.UTF8.GetBytes(gegeven ?? string.Empty));
        var rechts = SHA256.HashData(Encoding.UTF8.GetBytes(verwacht ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(links, rechts);
    }
}
