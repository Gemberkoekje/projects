using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Klaverjas.WebApp.Pages.Beheer;

/// <summary>
/// Dad's password for /beheer (D7). Typed once per computer; the cookie then lasts a year and slides,
/// so the Monday-evening visit never asks again.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(Toegang.RateLimitBeheer)]
public sealed class AanmeldenModel(IConfiguration configuration, InstellingenOphalenHandler instellingenOphalen, ILogger<AanmeldenModel> logger) : PageModel
{
    private readonly IConfiguration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly InstellingenOphalenHandler instellingenOphalen = instellingenOphalen ?? throw new ArgumentNullException(nameof(instellingenOphalen));
    private readonly ILogger<AanmeldenModel> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>The password as typed.</summary>
    [BindProperty]
    public string Wachtwoord { get; set; } = string.Empty;

    /// <summary>Why the last attempt failed, if it did.</summary>
    public string Fout { get; private set; } = string.Empty;

    /// <summary>Shows the form, or skips it when Dad is already signed in.</summary>
    /// <returns>The page, or Avonden.</returns>
    public IActionResult OnGet()
        => User.IsInRole(Toegang.Beheer) ? LocalRedirect("~/beheer") : Page();

    /// <summary>Checks the password and signs in.</summary>
    /// <param name="returnUrl">Where to go afterwards; only local addresses are honoured.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The return address, or the form with an error.</returns>
    public async Task<IActionResult> OnPostAsync(string returnUrl, CancellationToken cancellationToken)
    {
        var instellingen = (await instellingenOphalen.HandleAsync(new InstellingenOphalen(), cancellationToken)).Value;
        var configWachtwoord = Toegang.ConfigWachtwoord(configuration);

        // The players' password never opens /beheer, even if Dad's were ever the same.
        if (Toegang.KloptSpelers(Wachtwoord, instellingen))
        {
            logger.LogWarning("Aanmelden voor het beheer met het wachtwoord van de spelers vanaf {Adres}.", HttpContext.Connection.RemoteIpAddress);
            Fout = "Dit is het wachtwoord van de spelers. Voor het beheer heeft u een eigen wachtwoord.";
            return Page();
        }

        if (!Toegang.KloptBeheer(Wachtwoord, instellingen, configWachtwoord))
        {
            logger.LogWarning("Aanmelden voor het beheer mislukt vanaf {Adres}.", HttpContext.Connection.RemoteIpAddress);
            Fout = "Dat wachtwoord klopt niet. Let op hoofdletters.";
            return Page();
        }

        await Toegang.AanmeldenAsync(HttpContext, Toegang.Beheer, Toegang.Sleutel(Toegang.Beheer, instellingen, configWachtwoord));

        logger.LogInformation("Aangemeld voor het beheer vanaf {Adres}.", HttpContext.Connection.RemoteIpAddress);
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "~/beheer");
    }

    /// <summary>Signs out on this computer.</summary>
    /// <returns>The sign-in form.</returns>
    public async Task<IActionResult> OnPostAfmeldenAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage();
    }
}
