using Klaverjas.core.Tafels;

namespace Klaverjas.WebApp.Pages.Beheer;

/// <summary>
/// Kaartjes en tafelindeling (D12): who gets a card, who sits out (with a fair proposal), the cards to
/// print, and the table plan by seat number with its score sheets. The app never draws numbers (R12).
/// </summary>
public sealed class VoorbereidenModel(VoorbereidingOphalenHandler ophalen, VoorbereidingOpslaanHandler opslaan) : PageModel
{
    private readonly VoorbereidingOphalenHandler ophalen = ophalen ?? throw new ArgumentNullException(nameof(ophalen));
    private readonly VoorbereidingOpslaanHandler opslaan = opslaan ?? throw new ArgumentNullException(nameof(opslaan));

    /// <summary>The evening.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly Datum { get; set; }

    /// <summary>The number of players whose plan is shown; 0 for the expected number.</summary>
    [BindProperty(SupportsGet = true)]
    public int Aantal { get; set; }

    /// <summary>The members ticked for a card, as posted.</summary>
    [BindProperty]
    public List<Guid> Kaartjes { get; set; } = [];

    /// <summary>Blank cards, as posted.</summary>
    [BindProperty]
    public int Blanco { get; set; }

    /// <summary>The expected number of players, as posted.</summary>
    [BindProperty]
    public int Verwacht { get; set; }

    /// <summary>The slapers by number, as posted (an empty value for an empty place).</summary>
    [BindProperty]
    public List<string> Slapers { get; set; } = [];

    /// <summary>Why the last save was refused.</summary>
    [TempData]
    public string Fout { get; set; } = string.Empty;

    /// <summary>The screen.</summary>
    public VoorbereidingFormulier Formulier { get; private set; } = new VoorbereidingFormulier();

    /// <summary>The plan shown under "Tafelindeling".</summary>
    public Tafelschema Plan { get; private set; } = new Tafelschema();

    /// <summary>Shows the screen.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The page, or Avonden without a date.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Datum == default)
        {
            return RedirectToPage("/Beheer/Index");
        }

        Formulier = (await ophalen.HandleAsync(new VoorbereidingOphalen { Datum = Datum }, cancellationToken)).Value;
        Plan = Tafelschemas.Voor(Tafelschemas.KanMet(Aantal) ? Aantal : Formulier.Schema.Aantal);
        return Page();
    }

    /// <summary>Saves (the expected number changed, or "Bijwerken").</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The screen again.</returns>
    public async Task<IActionResult> OnPostOpslaanAsync(CancellationToken cancellationToken)
    {
        await OpslaanAsync(voorstellen: false, cancellationToken);
        return RedirectToPage(null, null, new { datum = Weergave.Iso(Datum) }, "slapers");
    }

    /// <summary>"Eerlijk voorstel".</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The screen with the empty places filled.</returns>
    public async Task<IActionResult> OnPostVoorstelAsync(CancellationToken cancellationToken)
    {
        await OpslaanAsync(voorstellen: true, cancellationToken);
        return RedirectToPage(null, null, new { datum = Weergave.Iso(Datum) }, "slapers");
    }

    /// <summary>"Kaartjes printen".</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The cards, ready to print.</returns>
    public async Task<IActionResult> OnPostKaartjesAsync(CancellationToken cancellationToken)
    {
        return await OpslaanAsync(voorstellen: false, cancellationToken)
            ? RedirectToPage("/Beheer/Afdrukken/Kaartjes", new { datum = Weergave.Iso(Datum) })
            : RedirectToPage(new { datum = Weergave.Iso(Datum) });
    }

    /// <summary>"Klaar, terug naar Avonden".</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Avonden.</returns>
    public async Task<IActionResult> OnPostKlaarAsync(CancellationToken cancellationToken)
    {
        return await OpslaanAsync(voorstellen: false, cancellationToken)
            ? RedirectToPage("/Beheer/Index")
            : RedirectToPage(new { datum = Weergave.Iso(Datum) });
    }

    private async Task<bool> OpslaanAsync(bool voorstellen, CancellationToken cancellationToken)
    {
        var result = await opslaan.HandleAsync(
            new VoorbereidingOpslaan
            {
                Datum = Datum,
                Kaartjes = Kaartjes,
                Blanco = Blanco,
                Verwacht = Verwacht,
                Slapers = [.. Slapers.Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty)],
                Voorstellen = voorstellen,
            },
            cancellationToken);

        if (!result.IsValid)
        {
            Fout = string.Join(" ", result.Messages.Select(m => m.Message ?? string.Empty));
        }

        return result.IsValid;
    }
}
