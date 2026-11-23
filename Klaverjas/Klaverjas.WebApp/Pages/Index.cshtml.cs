namespace Klaverjas.WebApp.Pages;

/// <summary>Avonden: the next evening with "Uitslag invullen", then the season so far.</summary>
public sealed class IndexModel(AvondenOphalenHandler avondenOphalen) : PageModel
{
    private readonly AvondenOphalenHandler avondenOphalen = avondenOphalen ?? throw new ArgumentNullException(nameof(avondenOphalen));

    /// <summary>The overview.</summary>
    public AvondenOverzicht Overzicht { get; private set; } = new AvondenOverzicht();

    /// <summary>Another evening to open, typed in the date field.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly Andere { get; set; }

    /// <summary>Loads the overview.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Overzicht = (await avondenOphalen.HandleAsync(new AvondenOphalen(), cancellationToken)).Value;
    }

    /// <summary>Opens the entry screen of the date typed under the list.</summary>
    /// <returns>The entry screen, or this page when no date was given.</returns>
    public IActionResult OnGetOpenen()
        => Andere == default ? RedirectToPage() : RedirectToPage("/Uitslag", new { datum = Weergave.Iso(Andere) });
}
