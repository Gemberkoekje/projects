using Qowaiv.Diagnostics.Contracts;

namespace Klaverjas.WebApp.Pages.Beheer;

/// <summary>
/// Uitslag invullen (D1): per player the card total and the pits, with a status. A half-typed
/// screen survives every round trip (adding a substitute, or a refused save) as typed.
/// </summary>
public sealed class UitslagModel(
    UitslagOphalenHandler ophalen,
    UitslagOpslaanHandler opslaan,
    SpelerToevoegenHandler spelerToevoegen,
    SpelersOphalenHandler spelersOphalen) : PageModel
{
    private readonly UitslagOphalenHandler ophalen = ophalen ?? throw new ArgumentNullException(nameof(ophalen));
    private readonly UitslagOpslaanHandler opslaan = opslaan ?? throw new ArgumentNullException(nameof(opslaan));
    private readonly SpelerToevoegenHandler spelerToevoegen = spelerToevoegen ?? throw new ArgumentNullException(nameof(spelerToevoegen));
    private readonly SpelersOphalenHandler spelersOphalen = spelersOphalen ?? throw new ArgumentNullException(nameof(spelersOphalen));

    /// <summary>The evening.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly Datum { get; set; }

    /// <summary>The rows as posted: members first, then substitutes.</summary>
    [BindProperty]
    public List<RegelInvoer> Regels { get; set; } = [];

    /// <summary>The substitute picked under "Invallers".</summary>
    [BindProperty]
    public Guid InvallerId { get; set; }

    /// <summary>The name of a substitute who is not in the list yet.</summary>
    [BindProperty]
    public string NieuweInvaller { get; set; } = string.Empty;

    /// <summary>What the screen shows.</summary>
    public UitslagFormulier Formulier { get; private set; } = new UitslagFormulier();

    /// <summary>Why the last save was refused.</summary>
    public IReadOnlyList<string> Meldingen { get; private set; } = [];

    /// <summary>Shows the evening, new or as saved.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The page, or Avonden for an evening without play.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Datum == default)
        {
            return RedirectToPage("/Beheer/Index");
        }

        var formulier = await ophalen.HandleAsync(new UitslagOphalen { Datum = Datum }, cancellationToken);
        if (!formulier.IsValid)
        {
            return RedirectToPage("/Beheer/Index");
        }

        Formulier = formulier.Value;
        return Page();
    }

    /// <summary>"Opslaan en stand bekijken".</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The standings, or the screen as typed with what still needs doing.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var command = new UitslagOpslaan { Datum = Datum, Regels = [.. Regels.Select(r => r.NaarInvoer())] };
        var result = await opslaan.HandleAsync(command, cancellationToken);

        if (result.IsValid)
        {
            return RedirectToPage("/Beheer/Stand", new { opgeslagen = Weergave.Iso(Datum) });
        }

        Meldingen = [.. result.Messages.Select(m => m.Message ?? string.Empty)];
        return await HerladenAsync(Regels, cancellationToken);
    }

    /// <summary>"Invaller toevoegen": a known substitute, or a new one by name.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The screen as typed, with the substitute's row added.</returns>
    public async Task<IActionResult> OnPostInvallerAsync(CancellationToken cancellationToken)
    {
        var regels = Regels.ToList();

        if (!string.IsNullOrWhiteSpace(NieuweInvaller))
        {
            var nieuw = await spelerToevoegen.HandleAsync(new SpelerToevoegen { Naam = NieuweInvaller, Soort = Soort.Invaller }, cancellationToken);
            if (nieuw.IsValid)
            {
                regels.Add(new RegelInvoer { SpelerId = nieuw.Value.Speler.Id, Status = Status.Speelt });
            }
            else
            {
                Meldingen = [.. nieuw.Messages.Select(m => m.Message ?? string.Empty)];
            }
        }
        else if (InvallerId != Guid.Empty && regels.TrueForAll(r => r.SpelerId != InvallerId))
        {
            regels.Add(new RegelInvoer { SpelerId = InvallerId, Status = Status.Speelt });
        }

        ModelState.Clear();
        return await HerladenAsync(regels, cancellationToken);
    }

    /// <summary>"Weghalen" next to a substitute.</summary>
    /// <param name="weghalen">The substitute.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The screen as typed, without that row.</returns>
    public async Task<IActionResult> OnPostWeghalenAsync(Guid weghalen, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        return await HerladenAsync([.. Regels.Where(r => r.SpelerId != weghalen)], cancellationToken);
    }

    private async Task<IActionResult> HerladenAsync(List<RegelInvoer> regels, CancellationToken cancellationToken)
    {
        var basis = await ophalen.HandleAsync(new UitslagOphalen { Datum = Datum }, cancellationToken);
        if (!basis.IsValid)
        {
            return RedirectToPage("/Beheer/Index");
        }

        var perId = (await spelersOphalen.HandleAsync(new SpelersOphalen(), cancellationToken)).Value.ToDictionary(s => s.Id);
        var rijen = regels
            .Where(r => perId.ContainsKey(r.SpelerId))
            .Select(r => new UitslagRegel
            {
                Speler = perId[r.SpelerId],
                Status = r.Status == Status.None ? Status.Speelt : r.Status,
                Punten = r.Punten ?? string.Empty,
                Pitten = r.Pitten ?? string.Empty,
                Ronde = r.Ronde,
            })
            .ToList();
        var opHetScherm = rijen.Select(r => r.Speler.Id).ToHashSet();

        Formulier = basis.Value with
        {
            Leden = [.. rijen.Where(r => r.Speler.IsLid)],
            Invallers = [.. rijen.Where(r => !r.Speler.IsLid)],
            AndereInvallers = [.. perId.Values
                .Where(s => s.Soort == Soort.Invaller && s.DoetMee && !opHetScherm.Contains(s.Id))
                .OrderBy(s => s.Naam, StandBerekening.NaamVolgorde)],
        };

        return Page();
    }
}

/// <summary>One posted row of the entry screen.</summary>
[Mutable("Model binding fills it in.")]
public sealed class RegelInvoer
{
    /// <summary>Who.</summary>
    public Guid SpelerId { get; set; }

    /// <summary>Played, sat out a round, or absent.</summary>
    public Status Status { get; set; }

    /// <summary>The card total as typed.</summary>
    public string Punten { get; set; } = string.Empty;

    /// <summary>The pits as typed.</summary>
    public string Pitten { get; set; } = string.Empty;

    /// <summary>The round a slaper sat out.</summary>
    public int Ronde { get; set; }

    /// <summary>The row as the command takes it.</summary>
    /// <returns>The row.</returns>
    public UitslagInvoer NaarInvoer() => new UitslagInvoer
    {
        SpelerId = SpelerId,
        Status = Status,
        Punten = Punten ?? string.Empty,
        Pitten = Pitten ?? string.Empty,
        Ronde = Ronde,
    };
}
