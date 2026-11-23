namespace Klaverjas.WebApp.Pages.Afdrukken;

/// <summary>The score cards of an evening, 12 per A4, in Dad's own layout (D15).</summary>
public sealed class KaartjesModel(KaartjesOphalenHandler ophalen) : PageModel
{
    /// <summary>Cards per A4: 3 across, 4 down.</summary>
    internal const int PerVel = 12;

    private readonly KaartjesOphalenHandler ophalen = ophalen ?? throw new ArgumentNullException(nameof(ophalen));

    /// <summary>The evening.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly Datum { get; set; }

    /// <summary>The cards.</summary>
    public Kaartjes Kaartjes { get; private set; } = new Kaartjes();

    /// <summary>The cards per A4.</summary>
    public IReadOnlyList<IReadOnlyList<Kaartje>> Vellen { get; private set; } = [];

    /// <summary>Loads the cards.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The page, or Avonden without a date.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Datum == default)
        {
            return RedirectToPage("/Index");
        }

        Kaartjes = (await ophalen.HandleAsync(new KaartjesOphalen { Datum = Datum }, cancellationToken)).Value;
        Vellen = [.. Kaartjes.Lijst.Chunk(PerVel)];
        return Page();
    }
}
