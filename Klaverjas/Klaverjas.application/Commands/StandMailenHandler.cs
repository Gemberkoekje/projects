using Klaverjas.application.Mail;
using Klaverjas.application.Online;

namespace Klaverjas.application.Commands;

/// <summary>
/// Mails the standings that are online (never newer ones: the mail and the players' page agree), one
/// mail per member with the PDF, replies to Dad. The test mail goes to Dad's own address only. Every
/// mailing is logged with how it went per member.
/// </summary>
public sealed class StandMailenHandler(
    IOnlineStandStore online,
    IAvondStore avonden,
    ISpelerStore spelers,
    IInstellingenStore instellingen,
    IVerzendingStore verzendingen,
    IStandPdfMaker pdf,
    IMailVerzender mail,
    ILogger<StandMailenHandler> logger)
{
    private readonly IOnlineStandStore online = online ?? throw new ArgumentNullException(nameof(online));
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly IVerzendingStore verzendingen = verzendingen ?? throw new ArgumentNullException(nameof(verzendingen));
    private readonly IStandPdfMaker pdf = pdf ?? throw new ArgumentNullException(nameof(pdf));
    private readonly IMailVerzender mail = mail ?? throw new ArgumentNullException(nameof(mail));
    private readonly ILogger<StandMailenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">Test or real, and the site's address.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The mailing as logged, or why nothing was sent.</returns>
    public Task<Result<Verzending>> HandleAsync(StandMailen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return MailenAsync(command, cancellationToken);
    }

    private async Task<Result<Verzending>> MailenAsync(StandMailen command, CancellationToken cancellationToken)
    {
        var instelling = await instellingen.LadenAsync(cancellationToken);
        var nuOnline = await online.LadenAsync(cancellationToken);
        var alleSpelers = await spelers.AllesAsync(cancellationToken);
        var live = HuidigeStand.Bereken(alleSpelers, await avonden.AllesAsync(cancellationToken), instelling);

        var bezwaar = Bezwaar(instelling, nuOnline, live);
        if (bezwaar.Length > 0)
        {
            return Result.WithMessages<Verzending>(ValidationMessage.Error(bezwaar));
        }

        var leden = Ontvangers.Leden(alleSpelers);
        if (!command.Proef && leden.Count == 0)
        {
            return Result.WithMessages<Verzending>(ValidationMessage.Error("Geen enkel lid heeft een e-mailadres. Vul die in bij Spelers."));
        }

        var eerder = await verzendingen.AllesAsync(cancellationToken);
        var onderwerp = StandMail.Onderwerp(instelling, nuOnline, Ontvangers.IsVerbeterd(eerder, nuOnline));
        var bijlage = pdf.Maak(nuOnline, instelling);

        // The test mail shows Dad the number 1's mail, sent to himself.
        var adressen = command.Proef
            ? [(Ontvangers.Voorbeeld(nuOnline, alleSpelers), instelling.Antwoordadres.Trim(), "Proef: " + onderwerp)]
            : leden.Select(lid => (lid, lid.Email.Trim(), onderwerp)).ToList();

        // The test mail is addressed to Dad, not to the member whose mail it shows.
        var berichten = adressen
            .Select(a => Bericht(nuOnline, instelling, a.Item1, a.Item2, a.Item3, bijlage, command.SiteAdres) with { NaarNaam = command.Proef ? string.Empty : a.Item1.Naam })
            .ToList();
        var uitslagen = await mail.VerstuurAsync(berichten, cancellationToken);

        var verstuurd = Clock.UtcNow();
        var verzending = new Verzending
        {
            Id = Verzending.IdVan(verstuurd),
            Datum = nuOnline.Stand.LaatsteAvond,
            Verstuurd = verstuurd,
            IsProef = command.Proef,
            Vingerafdruk = nuOnline.Vingerafdruk,
            Onderwerp = berichten[0].Onderwerp,
            Ontvangers = [.. adressen.Select((a, i) => new Ontvanger
            {
                SpelerId = command.Proef ? Guid.Empty : a.Item1.Id,
                Naam = command.Proef ? instelling.AfzenderNaam : a.Item1.Naam,
                Email = a.Item2,
                Gelukt = i < uitslagen.Count && uitslagen[i].Gelukt,
                Fout = i < uitslagen.Count ? uitslagen[i].Fout : "Niet verstuurd.",
            })],
        };

        await verzendingen.OpslaanAsync(verzending, cancellationToken);

        logger.LogInformation(
            "Stand na {Datum} gemaild ({Soort}): {Gelukt} van {Aantal} verstuurd.",
            verzending.Datum,
            command.Proef ? "proef" : "leden",
            verzending.Gelukt,
            verzending.Ontvangers.Count);

        return Result.For(verzending);
    }

    private string Bezwaar(Instellingen instelling, OnlineStand nuOnline, StandWeergave live)
    {
        if (!mail.IsIngesteld)
        {
            return "Mailen staat nog niet aan op de server. Vraag het de beheerder van de site.";
        }

        if (nuOnline.IsLeeg)
        {
            return "Zet de stand eerst online; daarna kunt u hem ook mailen.";
        }

        if (HuidigeStand.IsGewijzigd(nuOnline, live))
        {
            return "U heeft iets gewijzigd nadat de stand online is gezet. Zet eerst de verbeterde stand online; die gaat dan ook in de e-mail.";
        }

        return Emailadres.IsGeldig(instelling.Antwoordadres)
            ? string.Empty
            : "Vul eerst bij Instellingen uw eigen e-mailadres in, bij \"Antwoorden gaan naar\".";
    }

    private static MailBericht Bericht(OnlineStand nuOnline, Instellingen instelling, Speler lid, string naar, string onderwerp, ReadOnlyMemory<byte> bijlage, string siteAdres)
    {
        var (html, tekst) = StandMail.Inhoud(nuOnline, instelling, lid, siteAdres);
        return new MailBericht
        {
            Naar = naar,
            NaarNaam = lid.Naam,
            AfzenderNaam = instelling.AfzenderNaam,
            Antwoordadres = instelling.Antwoordadres.Trim(),
            Onderwerp = onderwerp,
            Html = html,
            Tekst = tekst,
            Bijlage = bijlage,
            BijlageNaam = StandMail.BijlageNaam(onderwerp.StartsWith("Proef: ", StringComparison.Ordinal) ? onderwerp[7..] : onderwerp),
        };
    }
}
