using Qowaiv.Diagnostics.Contracts;

namespace Klaverjas.WebApp.Pages.Beheer;

/// <summary>
/// Spelers: members and substitutes, changed in place (D14, D16). Nobody is deleted: "Gestopt" and
/// "Uit de stand" keep the scores and can always be undone.
/// </summary>
public sealed class SpelersModel(
    SpelersOphalenHandler spelersOphalen,
    SpelerToevoegenHandler toevoegen,
    SpelerAanpassenHandler aanpassen,
    SpelerWeghalenHandler weghalen) : PageModel
{
    private readonly SpelersOphalenHandler spelersOphalen = spelersOphalen ?? throw new ArgumentNullException(nameof(spelersOphalen));
    private readonly SpelerToevoegenHandler toevoegen = toevoegen ?? throw new ArgumentNullException(nameof(toevoegen));
    private readonly SpelerAanpassenHandler aanpassen = aanpassen ?? throw new ArgumentNullException(nameof(aanpassen));
    private readonly SpelerWeghalenHandler weghalen = weghalen ?? throw new ArgumentNullException(nameof(weghalen));

    /// <summary>Every player.</summary>
    public IReadOnlyList<Speler> Spelers { get; private set; } = [];

    /// <summary>The player being changed; <see cref="Guid.Empty"/> for none.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid Bewerk { get; set; }

    /// <summary>The substitute Dad is asked about before removing; <see cref="Guid.Empty"/> otherwise.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid Weghalen { get; set; }

    /// <summary>The changed player, as posted.</summary>
    [BindProperty]
    public SpelerFormulier Formulier { get; set; } = new SpelerFormulier();

    /// <summary>The confirmation of the last change.</summary>
    [TempData]
    public string Bevestiging { get; set; } = string.Empty;

    /// <summary>Why the last change was refused.</summary>
    public IReadOnlyList<string> Meldingen { get; private set; } = [];

    /// <summary>Shows the list.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LadenAsync(cancellationToken);
    }

    /// <summary>"Toevoegen" under "Nieuwe speler".</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The list with a confirmation, or with what is wrong.</returns>
    public async Task<IActionResult> OnPostToevoegenAsync(CancellationToken cancellationToken)
    {
        var result = await toevoegen.HandleAsync(new SpelerToevoegen { Naam = Formulier.Naam, Soort = Formulier.Soort, Email = Formulier.Email }, cancellationToken);

        if (!result.IsValid)
        {
            Meldingen = [.. result.Messages.Select(m => m.Message ?? string.Empty)];
            await LadenAsync(cancellationToken);
            return Page();
        }

        var speler = result.Value.Speler;
        Bevestiging = speler.IsLid
            ? $"{speler.Naam} is toegevoegd als lid. Voor de {result.Value.GemisteAvonden} avonden die dit seizoen al geweest zijn, krijgt {speler.Naam.Split(' ')[0]} de punten van afwezigheid."
            : $"{speler.Naam} is toegevoegd als invaller.";
        return RedirectToPage();
    }

    /// <summary>"Opslaan" in a changed row.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The list with a confirmation, or the row with what is wrong.</returns>
    public async Task<IActionResult> OnPostOpslaanAsync(CancellationToken cancellationToken)
    {
        var f = Formulier;
        var result = await aanpassen.HandleAsync(
            new SpelerAanpassen
            {
                Id = f.Id,
                Naam = f.Naam,
                Soort = f.Soort,
                Email = f.Email,
                Telefoon = f.Telefoon,
                Adres = f.Adres,
                Verjaardag = f.Verjaardag,
                DoetMee = f.DoetMee,
                InStand = f.InStand,
            },
            cancellationToken);

        if (!result.IsValid)
        {
            Meldingen = [.. result.Messages.Select(m => m.Message ?? string.Empty)];
            Bewerk = f.Id;
            await LadenAsync(cancellationToken);
            return Page();
        }

        await LadenAsync(cancellationToken);
        var opgeslagen = Spelers.First(s => s.Id == f.Id);
        Bevestiging = opgeslagen.IsLid && !opgeslagen.InStand
            ? $"Opgeslagen. {opgeslagen.Naam} staat niet meer in de stand van dit seizoen. De scores blijven bewaard; u kunt dit altijd terugzetten."
            : "Opgeslagen.";
        return RedirectToPage();
    }

    /// <summary>"Ja, weghalen" for a substitute.</summary>
    /// <param name="id">The substitute.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The list with a confirmation, or with what is wrong.</returns>
    public async Task<IActionResult> OnPostWeghalenAsync(Guid id, CancellationToken cancellationToken)
    {
        await LadenAsync(cancellationToken);
        var naam = Spelers.Where(s => s.Id == id).Select(s => s.Naam).FirstOrDefault() ?? "De invaller";
        var result = await weghalen.HandleAsync(new SpelerWeghalen { Id = id }, cancellationToken);

        if (!result.IsValid)
        {
            Meldingen = [.. result.Messages.Select(m => m.Message ?? string.Empty)];
            return Page();
        }

        var avonden = result.Value == 1 ? "avond" : "avonden";
        Bevestiging = result.Value == 0
            ? $"{naam} is weggehaald."
            : $"{naam} is weggehaald, ook uit {result.Value} {avonden}. Zet de stand opnieuw online als dat de winnaars van de laatste avond verandert.";
        return RedirectToPage();
    }

    private async Task LadenAsync(CancellationToken cancellationToken)
    {
        Spelers = (await spelersOphalen.HandleAsync(new SpelersOphalen(), cancellationToken)).Value;
    }
}

/// <summary>A player's fields as posted.</summary>
[Mutable("Model binding fills it in.")]
public sealed class SpelerFormulier
{
    /// <summary>Who; empty for a new player.</summary>
    public Guid Id { get; set; }

    /// <summary>First name and surname.</summary>
    public string Naam { get; set; } = string.Empty;

    /// <summary>Member or substitute.</summary>
    public Soort Soort { get; set; } = Soort.Lid;

    /// <summary>Email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Phone number.</summary>
    public string Telefoon { get; set; } = string.Empty;

    /// <summary>Postal address.</summary>
    public string Adres { get; set; } = string.Empty;

    /// <summary>Birthday.</summary>
    public string Verjaardag { get; set; } = string.Empty;

    /// <summary><see langword="false"/> for "Gestopt".</summary>
    public bool DoetMee { get; set; } = true;

    /// <summary>Whether the member stays in the stand.</summary>
    public bool InStand { get; set; } = true;
}
