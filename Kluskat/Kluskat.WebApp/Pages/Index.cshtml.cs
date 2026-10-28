namespace Kluskat.WebApp.Pages;

/// <summary>The klus screen: one klus, three buttons, nothing else.</summary>
public sealed class IndexModel(
    VolgendeKlusOphalenHandler volgende,
    ProfielOphalenHandler profiel,
    KlusGedaanMeldenHandler gedaan,
    KlusOverslaanHandler overslaan,
    OvergeslagenVrijgevenHandler vrijgeven,
    ILogger<IndexModel> logger) : PageModel
{
    /// <summary>What to show.</summary>
    public Keuze Keuze { get; private set; } = new Keuze();

    /// <summary>The level badge and the cat's look.</summary>
    public Profieloverzicht Profiel { get; private set; } = new Profieloverzicht();

    /// <summary>What the cat says.</summary>
    public string Aanmoediging { get; } = Weergave.Aanmoediging();

    /// <summary>A one-off message after a failed action, carried over the redirect.</summary>
    [TempData]
    public string Melding { get; set; } = string.Empty;

    /// <summary>Shows the klus to do.</summary>
    /// <param name="vooruit">Whether the user asked to get ahead.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(bool vooruit, CancellationToken cancellationToken)
    {
        var result = await volgende.HandleAsync(new VolgendeKlusOphalen { Vooruit = vooruit }, cancellationToken);

        if (result.IsValid)
        {
            Keuze = result.Value;
        }

        var stand = await profiel.HandleAsync(new ProfielOphalen(), cancellationToken);

        if (stand.IsValid)
        {
            Profiel = stand.Value;
        }
    }

    /// <summary>"Gedaan!".</summary>
    /// <param name="id">The klus.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The win screen, or back here with a message.</returns>
    public async Task<IActionResult> OnPostGedaanAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await gedaan.HandleAsync(new KlusGedaanMelden { KlusId = KlusId.Create((Uuid)id) }, cancellationToken);

        if (!result.IsValid)
        {
            return Terug(result);
        }

        return RedirectToPage("/Gedaan", new { id });
    }

    /// <summary>"Geen zin": any other klus.</summary>
    /// <param name="id">The klus on screen.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Back here, with the next klus.</returns>
    public Task<IActionResult> OnPostGeenZinAsync(Guid id, CancellationToken cancellationToken)
        => OverslaanAsync(id, OverslaanReden.GeenZin, cancellationToken);

    /// <summary>"Geen tijd": a smaller klus.</summary>
    /// <param name="id">The klus on screen.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Back here, with the next klus.</returns>
    public Task<IActionResult> OnPostGeenTijdAsync(Guid id, CancellationToken cancellationToken)
        => OverslaanAsync(id, OverslaanReden.GeenTijd, cancellationToken);

    /// <summary>"Laat ze nu toch zien": ends the skip window.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Back here.</returns>
    public async Task<IActionResult> OnPostVrijgevenAsync(CancellationToken cancellationToken)
    {
        var result = await vrijgeven.HandleAsync(new OvergeslagenVrijgeven(), cancellationToken);
        return result.IsValid ? RedirectToPage() : Terug(result);
    }

    private async Task<IActionResult> OverslaanAsync(Guid id, OverslaanReden reden, CancellationToken cancellationToken)
    {
        var result = await overslaan.HandleAsync(new KlusOverslaan { KlusId = KlusId.Create((Uuid)id), Reden = reden }, cancellationToken);
        return result.IsValid ? RedirectToPage() : Terug(result);
    }

    private RedirectToPageResult Terug(Result result)
    {
        Melding = string.Join(" ", result.Messages.Select(m => m.Message));
        logger.LogWarning("Actie op het klusscherm afgewezen: {Reden}", Melding);
        return RedirectToPage();
    }
}
