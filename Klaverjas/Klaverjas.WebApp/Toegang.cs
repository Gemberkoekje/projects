using Klaverjas.application.Ports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Klaverjas.WebApp;

/// <summary>
/// The two doors (D7): the players' password opens the standings, Dad's opens /beheer. One cookie
/// holds the role and a key derived from the password it was opened with, so changing a password
/// signs every other phone or computer out of that door.
/// </summary>
internal static class Toegang
{
    /// <summary>Dad's role: everything, the players' page included.</summary>
    public const string Beheer = "beheer";

    /// <summary>A player's role: the players' page only.</summary>
    public const string Speler = "speler";

    /// <summary>The authorization policy on /beheer.</summary>
    public const string BeheerBeleid = "beheer";

    /// <summary>The players' sign-in page.</summary>
    public const string SpelersAanmelden = "/aanmelden";

    /// <summary>Dad's sign-in page.</summary>
    public const string BeheerAanmelden = "/beheer/aanmelden";

    /// <summary>The rate-limit policy on the players' sign-in.</summary>
    public const string RateLimitSpelers = "aanmelden-spelers";

    /// <summary>The rate-limit policy on Dad's sign-in.</summary>
    public const string RateLimitBeheer = "aanmelden-beheer";

    private const string SleutelClaim = "klaverjas:sleutel";

    /// <summary>How long a sign-in lasts without use (D7).</summary>
    public static TimeSpan Geldigheid { get; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Dad's password from the configuration: his until he chooses his own in Instellingen, and after
    /// that the site keeper's way back in ("Wachtwoord vergeten? Bel de beheerder van de site").
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The password.</returns>
    public static string ConfigWachtwoord(IConfiguration configuration)
        => configuration["Auth:Password"] is { Length: > 0 } wachtwoord
            ? wachtwoord
            : throw new InvalidOperationException("Auth:Password is not set; without it nobody can sign in.");

    /// <summary>Whether a typed password opens /beheer.</summary>
    /// <param name="gegeven">What was typed.</param>
    /// <param name="instellingen">The settings with Dad's own password, if he chose one.</param>
    /// <param name="configWachtwoord">The password from the configuration.</param>
    /// <returns><see langword="true"/> for Dad's password or the configured one.</returns>
    public static bool KloptBeheer(string gegeven, Instellingen instellingen, string configWachtwoord)
        => Klopt(gegeven, configWachtwoord) || Wachtwoord.Klopt(gegeven, instellingen.BeheerWachtwoord);

    /// <summary>Whether a typed password opens the players' page: without capitals or spaces at the ends.</summary>
    /// <param name="gegeven">What was typed.</param>
    /// <param name="instellingen">The settings with the players' password.</param>
    /// <returns><see langword="true"/> when it matches; never while the page is closed.</returns>
    public static bool KloptSpelers(string gegeven, Instellingen instellingen)
        => instellingen.SpelerspaginaOpen && Klopt(Wachtwoord.Spelersvorm(gegeven), Wachtwoord.Spelersvorm(instellingen.Spelerswachtwoord));

    /// <summary>The key a cookie of a role must carry: it changes with that role's password.</summary>
    /// <param name="rol">The role.</param>
    /// <param name="instellingen">The settings.</param>
    /// <param name="configWachtwoord">The password from the configuration.</param>
    /// <returns>The key, or empty for an unknown role.</returns>
    public static string Sleutel(string rol, Instellingen instellingen, string configWachtwoord)
        => rol switch
        {
            Beheer => Vingerafdruk(Beheer, instellingen.BeheerWachtwoord.Length > 0 ? instellingen.BeheerWachtwoord : configWachtwoord),
            Speler => Vingerafdruk(Speler, Wachtwoord.Spelersvorm(instellingen.Spelerswachtwoord)),
            _ => string.Empty,
        };

    /// <summary>Signs in on this phone or computer for a year, sliding (D7).</summary>
    /// <param name="context">The request.</param>
    /// <param name="rol">The role.</param>
    /// <param name="sleutel">The role's current key.</param>
    /// <returns>A task that completes once the cookie is set.</returns>
    public static Task AanmeldenAsync(HttpContext context, string rol, string sleutel)
    {
        var identiteit = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, rol), new Claim(ClaimTypes.Role, rol), new Claim(SleutelClaim, sleutel)],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identiteit),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true });
    }

    /// <summary>
    /// Checks every cookie against the current passwords: a cookie opened with a password that has
    /// since changed is thrown away, so that phone or computer is asked for the new one.
    /// </summary>
    /// <param name="context">The cookie being validated.</param>
    /// <returns>A task that completes once checked.</returns>
    public static async Task ControleerAsync(CookieValidatePrincipalContext context)
    {
        var rol = context.Principal?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var sleutel = context.Principal?.FindFirstValue(SleutelClaim) ?? string.Empty;
        var services = context.HttpContext.RequestServices;
        var instellingen = await services.GetRequiredService<IInstellingenStore>().LadenAsync(context.HttpContext.RequestAborted);
        var verwacht = Sleutel(rol, instellingen, ConfigWachtwoord(services.GetRequiredService<IConfiguration>()));

        if (verwacht.Length == 0 || !Klopt(sleutel, verwacht))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    /// <summary>Sends a visitor to the right sign-in page: Dad's for /beheer, the players' for the rest.</summary>
    /// <param name="context">The redirect the cookie handler is about to make.</param>
    /// <returns>A completed task.</returns>
    public static Task NaarAanmelden(RedirectContext<CookieAuthenticationOptions> context)
    {
        var request = context.Request;

        if (request.Path.StartsWithSegments("/beheer", StringComparison.OrdinalIgnoreCase))
        {
            var terug = request.PathBase + request.Path + request.QueryString;
            context.Response.Redirect(request.PathBase.Add(BeheerAanmelden).Add(QueryString.Create("ReturnUrl", terug)));
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }

    /// <summary>Compares two strings in constant time.</summary>
    /// <param name="gegeven">What was typed.</param>
    /// <param name="verwacht">What it should be.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool Klopt(string gegeven, string verwacht)
    {
        // Hashing first makes both sides the same length, so the comparison time says nothing
        // about the length of the real password either.
        var links = SHA256.HashData(Encoding.UTF8.GetBytes(gegeven ?? string.Empty));
        var rechts = SHA256.HashData(Encoding.UTF8.GetBytes(verwacht ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(links, rechts);
    }

    private static string Vingerafdruk(string rol, string geheim)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rol + "\n" + geheim)));
}
