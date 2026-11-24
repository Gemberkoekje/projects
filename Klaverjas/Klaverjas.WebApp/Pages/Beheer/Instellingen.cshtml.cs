using Microsoft.Extensions.Configuration;
using Qowaiv.Diagnostics.Contracts;

namespace Klaverjas.WebApp.Pages.Beheer;

/// <summary>
/// Instellingen (D10): the club name, the absence score, the play day, the season and its evenings
/// without play, the colours, the players' password with its slips, and Dad's own password (D7).
/// Mail comes with its slice.
/// </summary>
public sealed class InstellingenModel(
    IConfiguration configuration,
    InstellingenOphalenHandler ophalen,
    InstellingenOpslaanHandler opslaan,
    GeenSpeelavondToevoegenHandler vrijToevoegen,
    GeenSpeelavondWeghalenHandler vrijWeghalen) : PageModel
{
    private readonly IConfiguration configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly InstellingenOphalenHandler ophalen = ophalen ?? throw new ArgumentNullException(nameof(ophalen));
    private readonly InstellingenOpslaanHandler opslaan = opslaan ?? throw new ArgumentNullException(nameof(opslaan));
    private readonly GeenSpeelavondToevoegenHandler vrijToevoegen = vrijToevoegen ?? throw new ArgumentNullException(nameof(vrijToevoegen));
    private readonly GeenSpeelavondWeghalenHandler vrijWeghalen = vrijWeghalen ?? throw new ArgumentNullException(nameof(vrijWeghalen));

    /// <summary>The settings as stored.</summary>
    public Instellingen Huidig { get; private set; } = new Instellingen();

    /// <summary>The settings as posted.</summary>
    [BindProperty]
    public InstellingenFormulier Formulier { get; set; } = new InstellingenFormulier();

    /// <summary>A new evening without play, as posted.</summary>
    [BindProperty]
    public DateOnly VrijDatum { get; set; }

    /// <summary>Its reason, as posted.</summary>
    [BindProperty]
    public string VrijReden { get; set; } = string.Empty;

    /// <summary>The confirmation of the last change.</summary>
    [TempData]
    public string Bevestiging { get; set; } = string.Empty;

    /// <summary>Why the last change was refused.</summary>
    public IReadOnlyList<string> Meldingen { get; private set; } = [];

    /// <summary>Why the uploaded workbook could not be read (set by the Inlezen page).</summary>
    [TempData]
    public string ExcelFout { get; set; } = string.Empty;

    /// <summary>Shows the settings.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LadenAsync(cancellationToken);
    }

    /// <summary>"Opslaan".</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The page with a confirmation, or with what is wrong.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var f = Formulier;
        var oudSpelerswachtwoord = (await ophalen.HandleAsync(new InstellingenOphalen(), cancellationToken)).Value.Spelerswachtwoord;
        var result = await opslaan.HandleAsync(
            new InstellingenOpslaan
            {
                Club = f.Club,
                PuntenAfwezig = f.PuntenAfwezig,
                Speeldag = f.Speeldag,
                EersteAvond = f.EersteAvond,
                Kleuren = new Kleuren { Eerste = f.KleurEerste, Tweede = f.KleurTweede, Derde = f.KleurDerde, Afwezig = f.KleurAfwezig },
                Spelerswachtwoord = f.Spelerswachtwoord,
                NieuwWachtwoord = f.NieuwWachtwoord,
                NieuwWachtwoordHerhaald = f.NieuwWachtwoordHerhaald,
            },
            cancellationToken);

        var bevestiging = "Opgeslagen. De stand rekent vanaf nu met deze instellingen.";

        if (result.IsValid)
        {
            var nieuw = (await ophalen.HandleAsync(new InstellingenOphalen(), cancellationToken)).Value;

            if (nieuw.Spelerswachtwoord != oudSpelerswachtwoord && nieuw.SpelerspaginaOpen)
            {
                bevestiging += " Het nieuwe wachtwoord voor de spelers geldt vanaf nu: print nieuwe briefjes.";
            }

            // The new password changes the key of Dad's cookie: sign in again here, so only his
            // other computers ask for it.
            if (!string.IsNullOrEmpty(f.NieuwWachtwoord))
            {
                await Toegang.AanmeldenAsync(HttpContext, Toegang.Beheer, Toegang.Sleutel(Toegang.Beheer, nieuw, Toegang.ConfigWachtwoord(configuration)));
                bevestiging += " Uw nieuwe wachtwoord geldt vanaf nu; op een andere computer moet u het één keer intypen.";
            }
        }

        return await AfrondenAsync(result, bevestiging, cancellationToken);
    }

    /// <summary>"Toevoegen" under "Geen klaverjassen op".</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The page with a confirmation, or with what is wrong.</returns>
    public async Task<IActionResult> OnPostVrijToevoegenAsync(CancellationToken cancellationToken)
    {
        var result = VrijDatum == default
            ? Result.WithMessages(ValidationMessage.Error("Kies eerst een datum."))
            : await vrijToevoegen.HandleAsync(new GeenSpeelavondToevoegen { Datum = VrijDatum, Reden = VrijReden }, cancellationToken);

        return await AfrondenAsync(result, "Toegevoegd.", cancellationToken);
    }

    /// <summary>"Weghalen" next to an evening without play.</summary>
    /// <param name="datum">The evening.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The page with a confirmation.</returns>
    public async Task<IActionResult> OnPostVrijWeghalenAsync(DateOnly datum, CancellationToken cancellationToken)
    {
        var result = await vrijWeghalen.HandleAsync(new GeenSpeelavondWeghalen { Datum = datum }, cancellationToken);
        return await AfrondenAsync(result, "Weggehaald: op die avond wordt weer geklaverjast.", cancellationToken);
    }

    private async Task<IActionResult> AfrondenAsync(Result result, string bevestiging, CancellationToken cancellationToken)
    {
        if (result.IsValid)
        {
            Bevestiging = bevestiging;
            return RedirectToPage();
        }

        Meldingen = [.. result.Messages.Select(m => m.Message ?? string.Empty)];
        await LadenAsync(cancellationToken);
        return Page();
    }

    private async Task LadenAsync(CancellationToken cancellationToken)
    {
        Huidig = (await ophalen.HandleAsync(new InstellingenOphalen(), cancellationToken)).Value;
    }
}

/// <summary>The settings as posted.</summary>
[Mutable("Model binding fills it in.")]
public sealed class InstellingenFormulier
{
    /// <summary>The club's name.</summary>
    public string Club { get; set; } = string.Empty;

    /// <summary>The absence score.</summary>
    public int PuntenAfwezig { get; set; }

    /// <summary>The club's weekday.</summary>
    public DayOfWeek Speeldag { get; set; } = DayOfWeek.Monday;

    /// <summary>The season's first evening.</summary>
    public DateOnly EersteAvond { get; set; }

    /// <summary>1st place.</summary>
    public string KleurEerste { get; set; } = string.Empty;

    /// <summary>2nd place.</summary>
    public string KleurTweede { get; set; } = string.Empty;

    /// <summary>3rd place.</summary>
    public string KleurDerde { get; set; } = string.Empty;

    /// <summary>Absent.</summary>
    public string KleurAfwezig { get; set; } = string.Empty;

    /// <summary>The players' password.</summary>
    public string Spelerswachtwoord { get; set; } = string.Empty;

    /// <summary>Dad's new password; empty keeps the current one.</summary>
    public string NieuwWachtwoord { get; set; } = string.Empty;

    /// <summary>Dad's new password, a second time.</summary>
    public string NieuwWachtwoordHerhaald { get; set; } = string.Empty;
}
