namespace Klaverjas.WebApp.Pages.Beheer.Afdrukken;

/// <summary>Slips with the address and the players' password, 10 per A4, to hand out on the evening (D5).</summary>
public sealed class BriefjesModel(InstellingenOphalenHandler ophalen) : PageModel
{
    /// <summary>Slips per A4: 2 across, 5 down.</summary>
    internal const int PerVel = 10;

    private readonly InstellingenOphalenHandler ophalen = ophalen ?? throw new ArgumentNullException(nameof(ophalen));

    /// <summary>The club's name.</summary>
    public string Club { get; private set; } = string.Empty;

    /// <summary>The players' password.</summary>
    public string Wachtwoord { get; private set; } = string.Empty;

    /// <summary>Loads the password.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The page, or Instellingen while there is no players' password.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var instellingen = (await ophalen.HandleAsync(new InstellingenOphalen(), cancellationToken)).Value;

        if (!instellingen.SpelerspaginaOpen)
        {
            return RedirectToPage("/Beheer/Instellingen", null, "spelerspagina");
        }

        Club = instellingen.Club;
        Wachtwoord = instellingen.Spelerswachtwoord;
        return Page();
    }
}
