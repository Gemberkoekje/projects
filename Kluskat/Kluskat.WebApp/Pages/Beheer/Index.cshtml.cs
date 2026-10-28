namespace Kluskat.WebApp.Pages.Beheer;

/// <summary>
/// The whole list, tucked away from the klus screen. It deliberately shows no due dates or overdue
/// markers: even here the list is a list of chores, not of things you have not done.
/// </summary>
public sealed class BeheerModel(KlussenOphalenHandler ophalen, KlusToevoegenHandler toevoegen) : PageModel
{
    /// <summary>Every active klus, by room.</summary>
    public IReadOnlyList<KlusOverzicht> Klussen { get; private set; } = [];

    /// <summary>The add form.</summary>
    [BindProperty]
    public KlusFormulier Nieuw { get; set; } = new KlusFormulier();

    /// <summary>When it was last done; empty makes it due straight away.</summary>
    [BindProperty]
    public string LaatstGedaanOp { get; set; } = string.Empty;

    /// <summary>Why adding failed.</summary>
    public IReadOnlyList<string> Fouten { get; private set; } = [];

    /// <summary>A one-off confirmation, carried over the redirect.</summary>
    [TempData]
    public string Melding { get; set; } = string.Empty;

    /// <summary>Shows the list.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once loaded.</returns>
    public Task OnGetAsync(CancellationToken cancellationToken) => LadenAsync(cancellationToken);

    /// <summary>Adds a klus.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The list again.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await toevoegen.HandleAsync(
            new KlusToevoegen { Data = Nieuw.Data(), LaatstGedaanOp = Formulieren.Datum(LaatstGedaanOp) },
            cancellationToken);

        if (!result.IsValid)
        {
            Fouten = [.. result.Messages.Select(m => m.Message ?? string.Empty)];
            await LadenAsync(cancellationToken);
            return Page();
        }

        Melding = $"Toegevoegd: {Nieuw.Ruimte} · {Nieuw.Naam}.";
        return RedirectToPage();
    }

    private async Task LadenAsync(CancellationToken cancellationToken)
    {
        var result = await ophalen.HandleAsync(new KlussenOphalen(), cancellationToken);

        if (result.IsValid)
        {
            Klussen = result.Value;
        }
    }
}
