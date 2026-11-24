namespace Klaverjas.WebApp.Pages.Beheer;

/// <summary>
/// The standings after the latest evening, exactly as the players will see them, and "Zet de stand
/// online" (D2). After a correction the button reads "Verbeterde stand online zetten".
/// </summary>
public sealed class StandModel(StandOphalenHandler standOphalen, StandOnlineZettenHandler onlineZetten) : PageModel
{
    private readonly StandOphalenHandler standOphalen = standOphalen ?? throw new ArgumentNullException(nameof(standOphalen));
    private readonly StandOnlineZettenHandler onlineZetten = onlineZetten ?? throw new ArgumentNullException(nameof(onlineZetten));

    /// <summary>The standings, the settings and what is online.</summary>
    public StandOverzicht Overzicht { get; private set; } = new StandOverzicht();

    /// <summary>The evening just saved, for the confirmation; <see langword="default"/> otherwise.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly Opgeslagen { get; set; }

    /// <summary>Dad's note, as posted.</summary>
    [BindProperty]
    public string Bericht { get; set; } = string.Empty;

    /// <summary>Whether the standings were just put online.</summary>
    [TempData]
    public bool NetOnline { get; set; }

    /// <summary>Why putting them online was refused.</summary>
    public string Fout { get; private set; } = string.Empty;

    /// <summary>Loads the standings.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LadenAsync(cancellationToken);

        // A correction of the week online keeps its note; a new week starts without one.
        var online = Overzicht.Online;
        Bericht = !online.IsLeeg && online.Stand.LaatsteAvond == Overzicht.Weergave.LaatsteAvond ? online.Bericht : string.Empty;
    }

    /// <summary>"Zet de stand online".</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page with the confirmation, or with what is wrong.</returns>
    public async Task<IActionResult> OnPostOnlineAsync(CancellationToken cancellationToken)
    {
        var result = await onlineZetten.HandleAsync(new StandOnlineZetten { Bericht = Bericht }, cancellationToken);

        if (result.IsValid)
        {
            NetOnline = true;
            return RedirectToPage();
        }

        Fout = string.Join(" ", result.Messages.Select(m => m.Message ?? string.Empty));
        await LadenAsync(cancellationToken);
        return Page();
    }

    private async Task LadenAsync(CancellationToken cancellationToken)
    {
        Overzicht = (await standOphalen.HandleAsync(new StandOphalen(), cancellationToken)).Value;
    }
}
