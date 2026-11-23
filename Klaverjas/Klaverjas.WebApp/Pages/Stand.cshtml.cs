namespace Klaverjas.WebApp.Pages;

/// <summary>
/// The standings after the latest evening, as the players will see them: Dad's check before they go
/// online (the "online" step itself comes with the players' page, slice 4).
/// </summary>
public sealed class StandModel(StandOphalenHandler standOphalen) : PageModel
{
    private readonly StandOphalenHandler standOphalen = standOphalen ?? throw new ArgumentNullException(nameof(standOphalen));

    /// <summary>The standings and the settings.</summary>
    public StandOverzicht Overzicht { get; private set; } = new StandOverzicht();

    /// <summary>The evening just saved, for the confirmation; <see langword="default"/> otherwise.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly Opgeslagen { get; set; }

    /// <summary>The club days of the latest evening's month: the month columns (R8).</summary>
    public IReadOnlyList<DateOnly> Maand { get; private set; } = [];

    /// <summary>Loads the standings.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Overzicht = (await standOphalen.HandleAsync(new StandOphalen(), cancellationToken)).Value;

        var laatste = Overzicht.Stand.LaatsteAvond;
        Maand = Overzicht.Stand.IsLeeg ? [] : Kalender.Speeldagen(laatste.Year, laatste.Month, Overzicht.Instellingen.Speeldag);
    }

    /// <summary>Why there is no play on a date, or empty.</summary>
    /// <param name="datum">The date.</param>
    /// <returns>The reason.</returns>
    public string GeenSpeelavond(DateOnly datum)
        => Overzicht.Instellingen.GeenSpeelavonden.Where(g => g.Datum == datum).Select(g => g.Reden).FirstOrDefault() ?? string.Empty;
}
