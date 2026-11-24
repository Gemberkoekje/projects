using Microsoft.AspNetCore.RateLimiting;

namespace Klaverjas.WebApp.Pages;

/// <summary>
/// The players' password (D5, D7): typed once from the slip, then the phone or computer stays signed
/// in for a year. Capitals and spaces at the ends do not matter.
/// </summary>
[AllowAnonymous]
[EnableRateLimiting(Toegang.RateLimitSpelers)]
public sealed class AanmeldenModel(InstellingenOphalenHandler instellingenOphalen, ILogger<AanmeldenModel> logger) : PageModel
{
    private readonly InstellingenOphalenHandler instellingenOphalen = instellingenOphalen ?? throw new ArgumentNullException(nameof(instellingenOphalen));
    private readonly ILogger<AanmeldenModel> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>The password as typed.</summary>
    [BindProperty]
    public string Wachtwoord { get; set; } = string.Empty;

    /// <summary>Why the last attempt failed, if it did.</summary>
    public string Fout { get; private set; } = string.Empty;

    /// <summary>The settings: the club's name, and whether the page is open yet.</summary>
    public Instellingen Instellingen { get; private set; } = new Instellingen();

    /// <summary>Shows the form, or skips it when already signed in.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The page, or the standings.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (User.Identity is { IsAuthenticated: true })
        {
            return LocalRedirect("~/");
        }

        Instellingen = (await instellingenOphalen.HandleAsync(new InstellingenOphalen(), cancellationToken)).Value;
        return Page();
    }

    /// <summary>Checks the password and signs in.</summary>
    /// <param name="returnUrl">Where to go afterwards; only local addresses are honoured.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The standings, or the form with an error.</returns>
    public async Task<IActionResult> OnPostAsync(string returnUrl, CancellationToken cancellationToken)
    {
        Instellingen = (await instellingenOphalen.HandleAsync(new InstellingenOphalen(), cancellationToken)).Value;

        if (!Instellingen.SpelerspaginaOpen)
        {
            return Page();
        }

        if (!Toegang.KloptSpelers(Wachtwoord, Instellingen))
        {
            logger.LogWarning("Aanmelden voor de stand mislukt vanaf {Adres}.", HttpContext.Connection.RemoteIpAddress);
            Fout = "Dat wachtwoord klopt niet. Kijk nog eens op het briefje.";
            return Page();
        }

        await Toegang.AanmeldenAsync(HttpContext, Toegang.Speler, Toegang.Sleutel(Toegang.Speler, Instellingen, string.Empty));

        logger.LogInformation("Speler aangemeld vanaf {Adres}.", HttpContext.Connection.RemoteIpAddress);
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "~/");
    }
}
