using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;

namespace CharacterCreator.WebApp.Endpoints;

/// <summary>Signing in and out: the two endpoints around the Vortex ERU.</summary>
internal static class AccountEndpoints
{
    /// <summary>Maps <c>GET /aanmelden</c> and <c>POST /afmelden</c>.</summary>
    /// <param name="endpoints">The app's endpoints.</param>
    /// <returns>The same endpoints, for chaining.</returns>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // With only one way in there is no page to choose from: straight on to the ERU, or to a page
        // that explains why there is no sign-in here (the demo has none).
        endpoints.MapGet("/aanmelden", SignInAsync).AllowAnonymous();

        // The form-bound parameter is what makes the antiforgery middleware validate the post.
        endpoints.MapPost("/afmelden", SignOutAsync)
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser());

        return endpoints;
    }

    /// <summary>Keeps a return URL inside this app; anything else becomes the build screen.</summary>
    /// <param name="pathBase">The app's path base.</param>
    /// <param name="returnUrl">The URL asked for; may be absent or hostile.</param>
    /// <returns>A local URL within the path base.</returns>
    internal static string LocalReturnUrl(PathString pathBase, string? returnUrl)
    {
        var home = $"{pathBase}/";

        if (string.IsNullOrEmpty(returnUrl)
            || !returnUrl.StartsWith('/')
            || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return home;
        }

        return returnUrl.StartsWith(home, StringComparison.Ordinal) || returnUrl == pathBase.Value
            ? returnUrl
            : home;
    }

    private static async Task<IResult> SignInAsync(HttpContext context, IAuthenticationSchemeProvider schemes, string? returnUrl)
    {
        if (await schemes.GetSchemeAsync(EruSignIn.Scheme) is null)
        {
            return Results.Redirect($"{context.Request.PathBase}/aanmelden-niet-ingesteld");
        }

        var properties = new AuthenticationProperties { RedirectUri = LocalReturnUrl(context.Request.PathBase, returnUrl) };
        return Results.Challenge(properties, [EruSignIn.Scheme]);
    }

    private static async Task<IResult> SignOutAsync(HttpContext context, IAuthenticationSchemeProvider schemes, [FromForm] string returnUrl)
    {
        var afterwards = $"{context.Request.PathBase}/afgemeld?returnUrl={Uri.EscapeDataString(LocalReturnUrl(context.Request.PathBase, returnUrl))}";
        var properties = new AuthenticationProperties { RedirectUri = afterwards };

        // The cookie first, the ERU last: the ERU's sign-out redirects to Keycloak to end that session
        // too, and only then comes back to the page above.
        return await schemes.GetSchemeAsync(EruSignIn.Scheme) is null
            ? Results.SignOut(properties, [CookieAuthenticationDefaults.AuthenticationScheme])
            : Results.SignOut(properties, [CookieAuthenticationDefaults.AuthenticationScheme, EruSignIn.Scheme]);
    }
}
