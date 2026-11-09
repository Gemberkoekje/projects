using System.Diagnostics.CodeAnalysis;

namespace DatumPrikker.WebApp.Pages;

/// <summary>The friendly face of an error status or an unhandled exception.</summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[SuppressMessage("Security Hotspot", "S4502:Ensure CSRF protection is not disabled", Justification = "The error page changes nothing. A failed POST (400 antiforgery, 429) is re-executed here as a POST and must still render.")]
public sealed class FoutModel : PageModel
{
    /// <summary>The status code that brought us here; 0 for an exception.</summary>
    [BindProperty(SupportsGet = true)]
    public int Code { get; set; }

    /// <summary>The heading.</summary>
    public string Titel => Code switch
    {
        StatusCodes.Status403Forbidden => "Geen toegang",
        StatusCodes.Status404NotFound => "Niet gevonden",
        StatusCodes.Status429TooManyRequests => "Even rustig aan",
        _ => "Er ging iets mis",
    };

    /// <summary>The explanation.</summary>
    public string Uitleg => Code switch
    {
        StatusCodes.Status403Forbidden => "Deze pagina is alleen voor de beheerder.",
        StatusCodes.Status404NotFound => "Deze pagina of prikker bestaat niet (meer). Controleer de link die je kreeg.",
        StatusCodes.Status429TooManyRequests => "Er kwamen vanaf jouw adres veel antwoorden achter elkaar binnen. Probeer het over een paar minuten opnieuw.",
        _ => "Probeer het zo nog eens. Blijft het misgaan, laat het de maker van de prikker weten.",
    };

    /// <summary>Shows the page; the exception, if any, was already logged by the middleware.</summary>
    public void OnGet()
    {
        // Nothing to load.
    }

    /// <summary>A re-executed POST (a 429 on answering) lands here too.</summary>
    public void OnPost()
    {
        // Nothing to do.
    }
}
