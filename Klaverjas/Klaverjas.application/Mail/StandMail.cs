using System.Globalization;
using System.Net;
using System.Text;

namespace Klaverjas.application.Mail;

/// <summary>
/// The standings mail (D2, the approved mock): "Beste …", Dad's note, the evening's winners, the
/// month's points and pits tables with the colours' legend, the address online, and the PDF attached.
/// No "Je staat …e" line: Dad dropped it (2026-11-25).
/// Inline styles only: mail programs drop style sheets. Everything typed is encoded.
/// </summary>
public static class StandMail
{
    private const string Rand = "border:1px solid #6f7a73;padding:2px 6px;white-space:nowrap;";
    private const string Kop = Rand + "background:#d3ebf7;font-size:11px;line-height:1.25;";
    private const string Cel = Rand + "text-align:center;";
    private const string Naam = Rand + "text-align:left;";
    private const string Tabel = "border-collapse:collapse;font-size:13px;background:#ffffff;";
    private const string Rest = "#e3f1da";

    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>The subject: Dad's subject with the week number, and "(verbeterd)" for a corrected mailing.</summary>
    /// <param name="instellingen">The subject setting.</param>
    /// <param name="online">The standings mailed.</param>
    /// <param name="verbeterd">Whether this week was mailed before with other numbers.</param>
    /// <returns>Such as "Standen week 47".</returns>
    public static string Onderwerp(Instellingen instellingen, OnlineStand online, bool verbeterd)
    {
        ArgumentNullException.ThrowIfNull(instellingen);
        ArgumentNullException.ThrowIfNull(online);

        return $"{instellingen.Onderwerp.Trim()} {Kalender.Week(online.Stand.LaatsteAvond)}" + (verbeterd ? " (verbeterd)" : string.Empty);
    }

    /// <summary>The attachment's file name: the subject, without characters a file name cannot hold.</summary>
    /// <param name="onderwerp">The subject.</param>
    /// <returns>Such as "Standen week 47.pdf".</returns>
    public static string BijlageNaam(string onderwerp)
    {
        var naam = new string([.. (onderwerp ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || " -_().".Contains(c, StringComparison.Ordinal))]).Trim();
        return (naam.Length == 0 ? "Stand" : naam) + ".pdf";
    }

    /// <summary>The mail for one member.</summary>
    /// <param name="online">The standings online.</param>
    /// <param name="instellingen">The sender, reply address, colours and club.</param>
    /// <param name="lid">The member it is about (their first name, place and pits).</param>
    /// <param name="siteAdres">The players' page, such as <c>https://klaverjas.gemberkoekje.nl</c>.</param>
    /// <returns>The HTML and the plain text.</returns>
    public static (string Html, string Tekst) Inhoud(OnlineStand online, Instellingen instellingen, Speler lid, string siteAdres)
    {
        ArgumentNullException.ThrowIfNull(online);
        ArgumentNullException.ThrowIfNull(instellingen);
        ArgumentNullException.ThrowIfNull(lid);

        return (Html(online, instellingen, lid, siteAdres ?? string.Empty), Tekst(online, instellingen, lid, siteAdres ?? string.Empty));
    }

    /// <summary>The first name, for "Beste …".</summary>
    /// <param name="naam">The full name.</param>
    /// <returns>The first word.</returns>
    public static string Voornaam(string naam)
        => (naam ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private static string Html(OnlineStand online, Instellingen instellingen, Speler lid, string siteAdres)
    {
        var stand = online.Stand;
        var h = new StringBuilder();
        h.Append("<!doctype html><html lang=\"nl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"></head>");
        h.Append("<body style=\"margin:0;padding:16px;background:#ffffff;color:#141a16;font-family:Verdana,Geneva,Arial,sans-serif;font-size:16px;line-height:1.5;\">");
        h.Append("<p style=\"margin:0 0 12px\">Beste ").Append(E(Voornaam(lid.Naam))).Append(",</p>");

        if (online.Bericht.Length > 0)
        {
            h.Append("<p style=\"margin:0 0 12px\">").Append(E(online.Bericht).Replace("\n", "<br>", StringComparison.Ordinal)).Append("</p>");
        }

        h.Append("<p style=\"margin:0 0 6px\"><b>Winnaars van ").Append(E(stand.LaatsteAvond.ToString("dddd d MMMM", Nl))).Append(":</b></p>");
        foreach (var winnaar in stand.Winnaars)
        {
            h.Append("<p style=\"margin:0 0 4px\">").Append(Medaille(winnaar.Plaats, instellingen.Kleuren)).Append(' ').Append(E(winnaar.Naam))
                .Append(winnaar.IsInvaller ? " (invaller)" : string.Empty).Append(" &nbsp;<b>").Append(Getal(winnaar.Punten)).Append("</b></p>");
        }

        PuntenTabel(h, stand, instellingen.Kleuren);
        PittenTabel(h, stand, instellingen.Kleuren);

        var host = Host(siteAdres);
        if (host.Length > 0)
        {
            h.Append("<p style=\"margin:16px 0 12px\">De stand staat altijd online op <a href=\"").Append(E(siteAdres)).Append("\">").Append(E(host))
                .Append("</a> (met het wachtwoord van de club). De PDF zit in de bijlage, om te printen.</p>");
        }

        h.Append("<p style=\"margin:0\">Groeten,<br>").Append(E(instellingen.AfzenderNaam)).Append("</p>");
        h.Append("</body></html>");
        return h.ToString();
    }

    private static string Tekst(OnlineStand online, Instellingen instellingen, Speler lid, string siteAdres)
    {
        var stand = online.Stand;
        var t = new StringBuilder();
        t.Append("Beste ").Append(Voornaam(lid.Naam)).Append(",\n\n");

        if (online.Bericht.Length > 0)
        {
            t.Append(online.Bericht).Append("\n\n");
        }

        t.Append("Winnaars van ").Append(stand.LaatsteAvond.ToString("dddd d MMMM", Nl)).Append(":\n");
        foreach (var winnaar in stand.Winnaars)
        {
            t.Append(winnaar.Plaats).Append(". ").Append(winnaar.Naam).Append(winnaar.IsInvaller ? " (invaller)" : string.Empty).Append(": ").Append(Getal(winnaar.Punten)).Append('\n');
        }

        t.Append("\nDe hele stand staat in de PDF in de bijlage");
        var host = Host(siteAdres);
        t.Append(host.Length > 0 ? $", en altijd online op {siteAdres} (met het wachtwoord van de club).\n\n" : ".\n\n");
        t.Append("Groeten,\n").Append(instellingen.AfzenderNaam).Append('\n');
        return t.ToString();
    }

    private static void PuntenTabel(StringBuilder h, StandWeergave stand, Kleuren kleuren)
    {
        Bijschrift(h, $"Punten {stand.Jaar}");
        h.Append("<table style=\"").Append(Tabel).Append("\"><thead><tr>");
        foreach (var kop in new[] { "Plaats", "Naam", "Totaal", "Verschil<br>met vorige" })
        {
            h.Append("<th rowspan=\"2\" style=\"").Append(Kop).Append("\">").Append(kop).Append("</th>");
        }

        MaandKop(h, stand, metDatum: true);
        h.Append("</thead><tbody>");

        foreach (var regel in stand.Punten)
        {
            h.Append("<tr><td style=\"").Append(Cel).Append("\">").Append(regel.Plaats).Append("</td>");
            h.Append("<td style=\"").Append(Naam).Append("\">").Append(E(regel.Naam)).Append("</td>");
            h.Append("<td style=\"").Append(Cel).Append("font-weight:bold;background:").Append(Kleur(regel.Plaats, kleuren, Rest)).Append(";\">").Append(Getal(regel.Totaal)).Append("</td>");
            h.Append("<td style=\"").Append(Cel).Append("font-weight:bold;background:#e8f2f8;\">").Append(Getal(regel.Verschil)).Append("</td>");
            PuntenCellen(h, stand, regel, kleuren);
            h.Append("</tr>");
        }

        foreach (var invaller in stand.Invallers)
        {
            h.Append("<tr style=\"font-style:italic;\"><td style=\"").Append(Cel).Append("\"></td><td style=\"").Append(Naam).Append("\">").Append(E(invaller.Naam))
                .Append(" (invaller)</td><td style=\"").Append(Cel).Append("\"></td><td style=\"").Append(Cel).Append("\"></td>");
            PuntenCellen(h, stand, invaller, kleuren);
            h.Append("</tr>");
        }

        h.Append("</tbody></table>");
        Legenda(h, kleuren);
    }

    private static void Legenda(StringBuilder h, Kleuren kleuren)
    {
        h.Append("<p style=\"margin:6px 0 0;font-size:13px;\">");
        foreach (var (tekst, kleur) in new[] { ("1e plaats", kleuren.Eerste), ("2e plaats", kleuren.Tweede), ("3e plaats", kleuren.Derde), ("Niet aanwezig", kleuren.Afwezig) })
        {
            h.Append("<span style=\"display:inline-block;margin:0 6px 4px 0;padding:1px 8px;border:1px solid #6f7a73;background:").Append(kleur).Append(";\">").Append(tekst).Append("</span>");
        }

        h.Append("</p>");
    }

    private static void PuntenCellen(StringBuilder h, StandWeergave stand, WeergaveRegel regel, Kleuren kleuren)
    {
        for (var i = 0; i < stand.Kolommen.Count; i++)
        {
            var kolom = stand.Kolommen[i];
            var cel = regel.Cel(i);
            if (kolom.IsVrij)
            {
                h.Append("<td style=\"").Append(Cel).Append("color:#46524a;font-style:italic;font-size:11px;\">").Append(E(kolom.Vrij)).Append("</td>");
            }
            else if (cel.IsLeeg)
            {
                h.Append("<td style=\"").Append(Cel).Append("\"></td>");
            }
            else
            {
                var achtergrond = cel.IsAfwezig ? kleuren.Afwezig : Kleur(cel.Plaats, kleuren, string.Empty);
                h.Append("<td style=\"").Append(Cel).Append(achtergrond.Length > 0 ? $"background:{achtergrond};" : string.Empty).Append("\">").Append(Getal(cel.Waarde)).Append("</td>");
            }
        }
    }

    private static void PittenTabel(StringBuilder h, StandWeergave stand, Kleuren kleuren)
    {
        Bijschrift(h, $"Pitten {stand.Jaar}");
        h.Append("<table style=\"").Append(Tabel).Append("\"><thead><tr>");
        foreach (var kop in new[] { "Plaats", "Naam", "Totaal" })
        {
            h.Append("<th rowspan=\"2\" style=\"").Append(Kop).Append("\">").Append(kop).Append("</th>");
        }

        MaandKop(h, stand, metDatum: false);
        h.Append("</thead><tbody>");

        foreach (var regel in stand.Pitten)
        {
            h.Append("<tr><td style=\"").Append(Cel).Append("\">").Append(regel.Plaats).Append("</td>");
            h.Append("<td style=\"").Append(Naam).Append("\">").Append(E(regel.Naam)).Append("</td>");
            h.Append("<td style=\"").Append(Cel).Append("font-weight:bold;background:").Append(Kleur(regel.Plaats, kleuren, Rest)).Append(";\">").Append(Getal(regel.Totaal)).Append("</td>");
            for (var i = 0; i < stand.Kolommen.Count; i++)
            {
                var cel = regel.Cel(i);
                h.Append("<td style=\"").Append(Cel).Append("\">").Append(cel.IsLeeg ? string.Empty : Getal(cel.Waarde)).Append("</td>");
            }

            h.Append("</tr>");
        }

        h.Append("</tbody><tfoot><tr><td style=\"").Append(Cel).Append("\"></td><td style=\"").Append(Naam).Append("font-weight:bold;\">Week totalen</td><td style=\"").Append(Cel).Append("\"></td>");
        foreach (var kolom in stand.Kolommen)
        {
            h.Append("<td style=\"").Append(Cel).Append("font-weight:bold;\">").Append(kolom.IsGespeeld ? Getal(kolom.PittenTotaal) : string.Empty).Append("</td>");
        }

        h.Append("</tr></tfoot></table>");
    }

    private static void MaandKop(StringBuilder h, StandWeergave stand, bool metDatum)
    {
        var maand = Nl.TextInfo.ToTitleCase(stand.LaatsteAvond.ToString("MMMM", Nl));
        h.Append("<th colspan=\"").Append(stand.Kolommen.Count).Append("\" style=\"").Append(Kop).Append("\">").Append(E(maand)).Append("</th></tr><tr>");

        foreach (var dag in stand.Kolommen.Select(k => k.Datum))
        {
            h.Append("<th style=\"").Append(Kop).Append("\">wk ").Append(Kalender.Week(dag));
            if (metDatum)
            {
                h.Append("<br>").Append(E(dag.ToString("d MMM", Nl).Replace(".", string.Empty, StringComparison.Ordinal)));
            }

            h.Append("</th>");
        }

        h.Append("</tr>");
    }

    private static void Bijschrift(StringBuilder h, string tekst)
        => h.Append("<p style=\"margin:18px 0 4px;font-size:17px;font-weight:bold;\">").Append(E(tekst)).Append("</p>");

    private static string Medaille(int plaats, Kleuren kleuren)
        => $"<span style=\"display:inline-block;width:24px;height:24px;line-height:24px;border-radius:12px;border:1px solid #6f7a73;text-align:center;font-weight:bold;font-size:13px;background:{Kleur(plaats, kleuren, "#ffffff")};\">{plaats}</span>";

    private static string Kleur(int plaats, Kleuren kleuren, string anders) => plaats switch
    {
        1 => kleuren.Eerste,
        2 => kleuren.Tweede,
        3 => kleuren.Derde,
        _ => anders,
    };

    private static string Host(string siteAdres)
        => Uri.TryCreate(siteAdres, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.Authority : string.Empty;

    private static string Getal(int getal) => getal.ToString(CultureInfo.InvariantCulture);

    private static string E(string tekst) => WebUtility.HtmlEncode(tekst ?? string.Empty);
}
