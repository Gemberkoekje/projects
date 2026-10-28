namespace Kluskat.WebApp.Pages.Beheer;

/// <summary>One klus: edit it, fill in a completion done without the app, or schrappen it.</summary>
public sealed class KlusBeheerModel(
    KlusOphalenHandler ophalen,
    KlusAanpassenHandler aanpassen,
    KlusGedaanOpHandler gedaanOp,
    KlusSchrappenHandler schrappen) : PageModel
{
    /// <summary>The klus as stored.</summary>
    public KlusOverzicht Klus { get; private set; } = KlusOverzicht.Geen;

    /// <summary>The edit form.</summary>
    [BindProperty]
    public KlusFormulier Formulier { get; set; } = new KlusFormulier();

    /// <summary>Why the last action failed.</summary>
    public IReadOnlyList<string> Fouten { get; private set; } = [];

    /// <summary>A one-off confirmation, carried over the redirect.</summary>
    [TempData]
    public string Melding { get; set; } = string.Empty;

    /// <summary>Shows the klus.</summary>
    /// <param name="id">The klus.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page, or the list when the klus is unknown.</returns>
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LadenAsync(id, cancellationToken))
        {
            return RedirectToPage("/Beheer/Index");
        }

        Formulier = KlusFormulier.Van(Klus);
        return Page();
    }

    /// <summary>Saves the edit form.</summary>
    /// <param name="id">The klus.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page again.</returns>
    public async Task<IActionResult> OnPostOpslaanAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await aanpassen.HandleAsync(new KlusAanpassen { KlusId = Id(id), Data = Formulier.Data() }, cancellationToken);
        return await AfrondenAsync(id, result, "Opgeslagen.", cancellationToken);
    }

    /// <summary>Fills in a completion done without the app.</summary>
    /// <param name="id">The klus.</param>
    /// <param name="datum">The local date, <c>yyyy-MM-dd</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page again.</returns>
    public async Task<IActionResult> OnPostGedaanOpAsync(Guid id, string datum, CancellationToken cancellationToken)
    {
        var dag = Formulieren.Datum(datum);
        var result = dag == default
            ? Result.WithMessages(ValidationMessage.Error("Kies een datum."))
            : await gedaanOp.HandleAsync(new KlusGedaanOp { KlusId = Id(id), Datum = dag }, cancellationToken);

        return await AfrondenAsync(id, result, "Bijgewerkt.", cancellationToken);
    }

    /// <summary>Takes the klus off the list.</summary>
    /// <param name="id">The klus.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The list, or the page with an error.</returns>
    public async Task<IActionResult> OnPostSchrappenAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await schrappen.HandleAsync(new KlusSchrappen { KlusId = Id(id) }, cancellationToken);

        if (!result.IsValid)
        {
            return await AfrondenAsync(id, result, string.Empty, cancellationToken);
        }

        Melding = $"Geschrapt: {Formulier.Ruimte} · {Formulier.Naam}.";
        return RedirectToPage("/Beheer/Index");
    }

    private static KlusId Id(Guid id) => KlusId.Create((Uuid)id);

    private async Task<IActionResult> AfrondenAsync(Guid id, Result result, string gelukt, CancellationToken cancellationToken)
    {
        if (result.IsValid)
        {
            Melding = gelukt;
            return RedirectToPage(new { id });
        }

        Fouten = [.. result.Messages.Select(m => m.Message ?? string.Empty)];
        return await LadenAsync(id, cancellationToken) ? Page() : RedirectToPage("/Beheer/Index");
    }

    private async Task<bool> LadenAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await ophalen.HandleAsync(new KlusOphalen { KlusId = Id(id) }, cancellationToken);

        if (!result.IsValid || result.Value.Geschrapt)
        {
            return false;
        }

        Klus = result.Value;
        return true;
    }
}
