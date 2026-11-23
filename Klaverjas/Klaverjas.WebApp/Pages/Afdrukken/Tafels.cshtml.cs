using Klaverjas.core.Tafels;

namespace Klaverjas.WebApp.Pages.Afdrukken;

/// <summary>
/// "Deze tafelindeling printen": the overview for the wall plus a score sheet per table per round, two
/// A5 forms per A4 (D18). "Alle aantallen printen": the overview for every number of players (D12).
/// </summary>
public sealed class TafelsModel(InstellingenOphalenHandler instellingen) : PageModel
{
    private readonly InstellingenOphalenHandler instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));

    /// <summary>The number of players.</summary>
    [BindProperty(SupportsGet = true)]
    public int Aantal { get; set; }

    /// <summary>The evening, printed on the sheets; <see langword="default"/> for none.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly Datum { get; set; }

    /// <summary>Every number of players, overviews only.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Alle { get; set; }

    /// <summary>The plans to print.</summary>
    public IReadOnlyList<Tafelschema> Schemas { get; private set; } = [];

    /// <summary>The club's name.</summary>
    public string Club { get; private set; } = string.Empty;

    /// <summary>Loads the plans.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Club = (await instellingen.HandleAsync(new InstellingenOphalen(), cancellationToken)).Value.Club;
        Schemas = Alle
            ? [.. Tafelschemas.Aantallen.Select(Tafelschemas.Voor)]
            : [Tafelschemas.Voor(Tafelschemas.Dichtstbij(Aantal))];
    }

    /// <summary>The score sheets of a plan: one per table per round.</summary>
    /// <param name="schema">The plan.</param>
    /// <returns>Per sheet the round (1–3) and the table.</returns>
    public static IEnumerable<(int Ronde, Tafel Tafel)> Bladen(Tafelschema schema)
        => schema.Rondes.SelectMany((ronde, r) => ronde.Tafels.Select(t => (r + 1, t)));
}
