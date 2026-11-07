using CharacterCreator.WebApp.Client.Words;
using CharacterCreator.WebApp.Localization;
using Microsoft.AspNetCore.Builder;

namespace CharacterCreator.WebApp.Endpoints;

/// <summary>Switching the language: NL or EN in the crest bar (DESIGN.md §5).</summary>
internal static class LanguageEndpoints
{
    /// <summary>Maps <c>GET /taal/{code}</c>.</summary>
    /// <param name="endpoints">The app's endpoints.</param>
    /// <returns>The same endpoints, for chaining.</returns>
    public static IEndpointRouteBuilder MapLanguageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // A plain link, so it works without JavaScript; the whole page comes back in the new language.
        // The plan being built survives, as it is kept in the browser.
        endpoints.MapGet("/taal/{code}", Switch).AllowAnonymous();
        return endpoints;
    }

    /// <summary>Stores the language in a cookie for a year, and goes back to the page it came from.</summary>
    private static IResult Switch(HttpContext context, string code, string? returnUrl)
    {
        var ui = Ui.For(code);
        context.Response.Cookies.Append(CookieUiLanguage.CookieName, ui.Code, new CookieOptions
        {
            MaxAge = TimeSpan.FromDays(365),
            Path = $"{context.Request.PathBase}/",
            SameSite = SameSiteMode.Lax,

            // Always: the demo and production sit behind TLS, and browsers keep a secure cookie on
            // http://localhost too.
            Secure = true,
            HttpOnly = true,
            IsEssential = true,
        });

        return Results.LocalRedirect(AccountEndpoints.LocalReturnUrl(context.Request.PathBase, returnUrl));
    }
}
