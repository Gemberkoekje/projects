namespace Klaverjas.WebApp.Pages.Beheer;

/// <summary>
/// The standings after the latest evening, exactly as the players will see them, and "Zet de stand
/// online" (D2). After a correction the button reads "Verbeterde stand online zetten". Below it the
/// optional extra: "Ook e-mailen" with the PDF, after a test mail if Dad likes.
/// </summary>
public sealed class StandModel(
    StandOphalenHandler standOphalen,
    StandOnlineZettenHandler onlineZetten,
    MailOphalenHandler mailOphalen,
    StandMailenHandler mailen) : PageModel
{
    private readonly StandOphalenHandler standOphalen = standOphalen ?? throw new ArgumentNullException(nameof(standOphalen));
    private readonly StandOnlineZettenHandler onlineZetten = onlineZetten ?? throw new ArgumentNullException(nameof(onlineZetten));
    private readonly MailOphalenHandler mailOphalen = mailOphalen ?? throw new ArgumentNullException(nameof(mailOphalen));
    private readonly StandMailenHandler mailen = mailen ?? throw new ArgumentNullException(nameof(mailen));

    /// <summary>The standings, the settings and what is online.</summary>
    public StandOverzicht Overzicht { get; private set; } = new StandOverzicht();

    /// <summary>The mail card.</summary>
    public MailOverzicht Mail { get; private set; } = new MailOverzicht();

    /// <summary>The evening just saved, for the confirmation; <see langword="default"/> otherwise.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly Opgeslagen { get; set; }

    /// <summary>"mailen" while Dad is asked to confirm the mailing.</summary>
    [BindProperty(SupportsGet = true)]
    public string Stap { get; set; } = string.Empty;

    /// <summary>Dad's note, as posted.</summary>
    [BindProperty]
    public string Bericht { get; set; } = string.Empty;

    /// <summary>Whether the standings were just put online.</summary>
    [TempData]
    public bool NetOnline { get; set; }

    /// <summary>How the last mailing went, in a sentence.</summary>
    [TempData]
    public string MailUitslag { get; set; } = string.Empty;

    /// <summary>Whether the last mailing had failures.</summary>
    [TempData]
    public bool MailMislukt { get; set; }

    /// <summary>Why putting them online was refused.</summary>
    public string Fout { get; private set; } = string.Empty;

    /// <summary>Why mailing was refused.</summary>
    public string MailFout { get; private set; } = string.Empty;

    /// <summary>Whether Dad is asked to confirm the mailing.</summary>
    public bool Bevestigen => Stap == "mailen";

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

        return await FoutAsync(result.Messages, mail: false, cancellationToken);
    }

    /// <summary>"Ja, versturen": the mail to every member with an address.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page with how it went.</returns>
    public Task<IActionResult> OnPostMailenAsync(CancellationToken cancellationToken)
        => MailenAsync(proef: false, cancellationToken);

    /// <summary>"Proefmail naar mij": the number 1's mail, to Dad's own address.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>This page with how it went.</returns>
    public Task<IActionResult> OnPostProefAsync(CancellationToken cancellationToken)
        => MailenAsync(proef: true, cancellationToken);

    /// <summary>"PDF bekijken": the PDF of the standings online, opened in the browser.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The PDF, or this page when nothing is online.</returns>
    public async Task<IActionResult> OnGetPdfAsync(CancellationToken cancellationToken)
    {
        var result = await mailOphalen.HandleAsync(new StandPdfOphalen(), cancellationToken);
        if (!result.IsValid)
        {
            return RedirectToPage();
        }

        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(result.Value.Bestandsnaam)}";
        return File(result.Value.Inhoud.ToArray(), "application/pdf");
    }

    private async Task<IActionResult> MailenAsync(bool proef, CancellationToken cancellationToken)
    {
        var result = await mailen.HandleAsync(new StandMailen { Proef = proef, SiteAdres = $"{Request.Scheme}://{Request.Host}" }, cancellationToken);

        if (!result.IsValid)
        {
            return await FoutAsync(result.Messages, mail: true, cancellationToken);
        }

        var verzending = result.Value;
        var mislukt = verzending.Mislukt.ToList();
        MailMislukt = mislukt.Count > 0;
        MailUitslag = Samenvatting(verzending, mislukt);
        return RedirectToPage(null, null, null, "mail");
    }

    private static string Samenvatting(Verzending verzending, List<Ontvanger> mislukt)
    {
        if (verzending.IsProef)
        {
            return mislukt.Count == 0
                ? $"✓ De proefmail is naar {verzending.Ontvangers[0].Email} gestuurd. Kijk in uw mailbox hoe hij eruitziet."
                : $"De proefmail kon niet weg: {mislukt[0].Fout}";
        }

        if (mislukt.Count == 0)
        {
            return $"✓ De e-mail is verstuurd aan {verzending.Gelukt} {(verzending.Gelukt == 1 ? "lid" : "leden")}.";
        }

        var namen = string.Join(", ", mislukt.Select(o => o.Naam));
        return $"{verzending.Gelukt} van de {verzending.Ontvangers.Count} e-mails zijn verstuurd. Niet gelukt bij: {namen}. Kijk of hun e-mailadres klopt (bij Spelers) of print de PDF voor hen.";
    }

    private async Task<IActionResult> FoutAsync(IEnumerable<IValidationMessage> meldingen, bool mail, CancellationToken cancellationToken)
    {
        var tekst = string.Join(" ", meldingen.Select(m => m.Message ?? string.Empty));
        Fout = mail ? string.Empty : tekst;
        MailFout = mail ? tekst : string.Empty;
        Stap = string.Empty;
        await LadenAsync(cancellationToken);
        return Page();
    }

    private async Task LadenAsync(CancellationToken cancellationToken)
    {
        Overzicht = (await standOphalen.HandleAsync(new StandOphalen(), cancellationToken)).Value;
        Mail = (await mailOphalen.HandleAsync(new MailOverzichtOphalen(), cancellationToken)).Value;
    }
}
