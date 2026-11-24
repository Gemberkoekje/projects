using Klaverjas.application.Mail;

namespace Klaverjas.application.Queries;

/// <summary>Everything the mail card, the mail preview and "PDF bekijken" show, always about the standings online.</summary>
public sealed class MailOphalenHandler(
    IOnlineStandStore online,
    ISpelerStore spelers,
    IInstellingenStore instellingen,
    IVerzendingStore verzendingen,
    IStandPdfMaker pdf,
    IMailVerzender mail)
{
    private readonly IOnlineStandStore online = online ?? throw new ArgumentNullException(nameof(online));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly IVerzendingStore verzendingen = verzendingen ?? throw new ArgumentNullException(nameof(verzendingen));
    private readonly IStandPdfMaker pdf = pdf ?? throw new ArgumentNullException(nameof(pdf));
    private readonly IMailVerzender mail = mail ?? throw new ArgumentNullException(nameof(mail));

    /// <summary>The mail card.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Who would get it, who has no address, and what was sent this week.</returns>
    public Task<Result<MailOverzicht>> HandleAsync(MailOverzichtOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OverzichtAsync(cancellationToken);
    }

    /// <summary>The mail as the number 1 gets it.</summary>
    /// <param name="query">The site's address.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The mail, or why there is none yet.</returns>
    public Task<Result<MailVoorbeeld>> HandleAsync(MailVoorbeeldOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return VoorbeeldAsync(query, cancellationToken);
    }

    /// <summary>The PDF of the standings online.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The PDF, or why there is none yet.</returns>
    public Task<Result<StandPdf>> HandleAsync(StandPdfOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return PdfAsync(cancellationToken);
    }

    private async Task<Result<MailOverzicht>> OverzichtAsync(CancellationToken cancellationToken)
    {
        var instelling = await instellingen.LadenAsync(cancellationToken);
        var nuOnline = await online.LadenAsync(cancellationToken);
        var alleSpelers = await spelers.AllesAsync(cancellationToken);
        var alle = await verzendingen.AllesAsync(cancellationToken);
        var dezeWeek = nuOnline.IsLeeg ? [] : Ontvangers.VanDezeWeek(alle, nuOnline);
        var proeven = nuOnline.IsLeeg ? [] : alle.Where(v => v.IsProef && v.Datum == nuOnline.Stand.LaatsteAvond).OrderByDescending(v => v.Verstuurd).ToList();
        var verbeterd = !nuOnline.IsLeeg && Ontvangers.IsVerbeterd(alle, nuOnline);

        return Result.For(new MailOverzicht
        {
            IsIngesteld = mail.IsIngesteld,
            Antwoordadres = instelling.Antwoordadres,
            Ontvangers = Ontvangers.Leden(alleSpelers).Count,
            ZonderEmail = Ontvangers.ZonderEmail(alleSpelers),
            LaatsteVerzending = dezeWeek.FirstOrDefault() ?? new Verzending(),
            LaatsteProef = proeven.FirstOrDefault() ?? new Verzending(),
            AlGemaild = dezeWeek.Any(v => v.Vingerafdruk == nuOnline.Vingerafdruk),
            Verbeterd = verbeterd,
            Onderwerp = nuOnline.IsLeeg ? string.Empty : StandMail.Onderwerp(instelling, nuOnline, verbeterd),
        });
    }

    private async Task<Result<MailVoorbeeld>> VoorbeeldAsync(MailVoorbeeldOphalen query, CancellationToken cancellationToken)
    {
        var nuOnline = await online.LadenAsync(cancellationToken);
        if (nuOnline.IsLeeg)
        {
            return Result.WithMessages<MailVoorbeeld>(ValidationMessage.Error("Er staat nog geen stand online, dus er is nog geen e-mail."));
        }

        var instelling = await instellingen.LadenAsync(cancellationToken);
        var alleSpelers = await spelers.AllesAsync(cancellationToken);
        var lid = Ontvangers.Voorbeeld(nuOnline, alleSpelers);
        var onderwerp = StandMail.Onderwerp(instelling, nuOnline, Ontvangers.IsVerbeterd(await verzendingen.AllesAsync(cancellationToken), nuOnline));

        return Result.For(new MailVoorbeeld
        {
            Onderwerp = onderwerp,
            Van = instelling.AfzenderNaam,
            Aan = Emailadres.IsGeldig(lid.Email) ? lid.Email : $"{lid.Naam} (geen e-mailadres)",
            Antwoordadres = instelling.Antwoordadres,
            BijlageNaam = StandMail.BijlageNaam(onderwerp),
            Html = StandMail.Inhoud(nuOnline, instelling, lid, query.SiteAdres).Html,
        });
    }

    private async Task<Result<StandPdf>> PdfAsync(CancellationToken cancellationToken)
    {
        var nuOnline = await online.LadenAsync(cancellationToken);
        if (nuOnline.IsLeeg)
        {
            return Result.WithMessages<StandPdf>(ValidationMessage.Error("Er staat nog geen stand online, dus er is nog geen PDF."));
        }

        var instelling = await instellingen.LadenAsync(cancellationToken);
        return Result.For(new StandPdf
        {
            Inhoud = pdf.Maak(nuOnline, instelling),
            Bestandsnaam = StandMail.BijlageNaam(StandMail.Onderwerp(instelling, nuOnline, verbeterd: false)),
        });
    }
}
