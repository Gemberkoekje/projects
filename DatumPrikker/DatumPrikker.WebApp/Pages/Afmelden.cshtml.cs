using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DatumPrikker.WebApp.Pages;

/// <summary>Signs out (POST only; the GET just shows a button).</summary>
[AllowAnonymous]
public sealed class AfmeldenModel : PageModel
{
    /// <summary>Shows the button.</summary>
    public void OnGet()
    {
        // Signing out on a GET would let any link sign you out.
    }

    /// <summary>Signs out and goes home.</summary>
    /// <returns>A redirect to the landing page.</returns>
    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return LocalRedirect("/");
    }
}
