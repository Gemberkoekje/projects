namespace Kluskat.WebApp.Pages;

/// <summary>
/// The win screen. It never shows the next klus by itself: "Nog eentje" is a choice, and so is
/// "Klaar voor nu".
/// </summary>
public sealed class GedaanModel(
    OverwinningOphalenHandler ophalen,
    GedaanTerugdraaienHandler terugdraaien,
    ILogger<GedaanModel> logger) : PageModel
{
    /// <summary>What was done, and how many this week.</summary>
    public Overwinning Overwinning { get; private set; } = new Overwinning();

    /// <summary>What the cat says.</summary>
    public string Juich { get; } = Weergave.Juich();

    /// <summary>Shows the win.</summary>
    /// <param name="id">The klus that was just done.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page, or the klus screen when the klus is unknown.</returns>
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await ophalen.HandleAsync(new OverwinningOphalen { KlusId = KlusId.Create((Uuid)id) }, cancellationToken);

        if (!result.IsValid)
        {
            return RedirectToPage("/Index");
        }

        Overwinning = result.Value;
        return Page();
    }

    /// <summary>"Oeps, niet gedaan".</summary>
    /// <param name="id">The klus.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The klus screen.</returns>
    public async Task<IActionResult> OnPostTerugdraaienAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await terugdraaien.HandleAsync(new GedaanTerugdraaien { KlusId = KlusId.Create((Uuid)id) }, cancellationToken);

        if (!result.IsValid)
        {
            logger.LogWarning("Terugdraaien van klus {KlusId} afgewezen.", id);
        }

        return RedirectToPage("/Index");
    }
}
