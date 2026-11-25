using Klaverjas.core.Standen;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace Klaverjas.infrastructure.Adapters.Pdf;

/// <summary>
/// The PDF of the standings online, like Dad's month sheet: one A4 landscape with the points and the
/// pits side by side, the month's weeks as columns, the sheet's colours. Scaled to fit one page.
/// QuestPDF runs under its Community licence (individual use) and brings its own font (Lato), so the
/// cluster's container needs no fonts.
/// </summary>
public sealed class QuestPdfStandMaker : IStandPdfMaker
{
    private const string Lijn = "#6f7a73";
    private const string KopKleur = "#d3ebf7";
    private const string Rest = "#e3f1da";
    private const string VerschilKleur = "#e8f2f8";

    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    static QuestPdfStandMaker()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Maak(OnlineStand online, Instellingen instellingen) => Opbouw(online, instellingen).GeneratePdf();

    /// <summary>The document itself (the specs count its pages).</summary>
    /// <param name="online">The standings online.</param>
    /// <param name="instellingen">The club's name and the colours.</param>
    /// <returns>The document.</returns>
    internal static Document Opbouw(OnlineStand online, Instellingen instellingen)
    {
        ArgumentNullException.ThrowIfNull(online);
        ArgumentNullException.ThrowIfNull(instellingen);

        var stand = online.Stand;
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(stijl => stijl.FontSize(9).FontColor("#141a16"));
            page.Content().Column(kolom =>
            {
                kolom.Spacing(5);
                kolom.Item().Row(rij =>
                {
                    rij.RelativeItem().Text(instellingen.Club).FontSize(15).Bold();
                    rij.AutoItem().AlignBottom().Text($"Stand na week {Kalender.Week(stand.LaatsteAvond)} · {Hoofdletter(stand.LaatsteAvond.ToString("dddd d MMMM yyyy", Nl))}").FontSize(11);
                });

                if (online.Bericht.Length > 0)
                {
                    kolom.Item().Text(online.Bericht).Italic();
                }

                kolom.Item().Text(tekst =>
                {
                    tekst.Span("Winnaars van de avond:  ").SemiBold();
                    foreach (var winnaar in stand.Winnaars)
                    {
                        tekst.Span($"{winnaar.Plaats}. {winnaar.Naam}{(winnaar.IsInvaller ? " (invaller)" : string.Empty)} ").BackgroundColor(Kleur(winnaar.Plaats, instellingen.Kleuren, "#ffffff"));
                        tekst.Span($"{Getal(winnaar.Punten)}     ").Bold();
                    }
                });

                kolom.Item().ScaleToFit().Row(rij =>
                {
                    rij.Spacing(14);
                    rij.RelativeItem(stand.Kolommen.Count + 6).Element(c => PuntenTabel(c, stand, instellingen.Kleuren));
                    rij.RelativeItem(stand.Kolommen.Count + 4).Element(c => PittenTabel(c, stand, instellingen.Kleuren));
                });
            });
        }));
    }

    // The colours' legend, right under the points table (Dad, 2026-11-25).
    private static void Legenda(IContainer container, Kleuren kleuren)
    {
        container.PaddingTop(5).Row(rij =>
        {
            rij.Spacing(6);
            foreach (var (tekst, kleur) in new[] { ("1e plaats", kleuren.Eerste), ("2e plaats", kleuren.Tweede), ("3e plaats", kleuren.Derde), ("Niet aanwezig", kleuren.Afwezig) })
            {
                rij.AutoItem().Border(0.5f).BorderColor(Lijn).Background(kleur).PaddingHorizontal(6).PaddingVertical(1.5f).Text(tekst).FontSize(9);
            }
        });
    }

    private static void PuntenTabel(IContainer container, StandWeergave stand, Kleuren kleuren)
    {
        container.Column(kolom =>
        {
            kolom.Item().PaddingBottom(2).Text($"Punten {stand.Jaar}").FontSize(12).Bold();
            kolom.Item().Table(tabel =>
            {
                tabel.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(30);
                    c.RelativeColumn(4);
                    c.ConstantColumn(44);
                    c.ConstantColumn(44);
                    for (var i = 0; i < stand.Kolommen.Count; i++)
                    {
                        c.RelativeColumn(1);
                    }
                });

                tabel.Header(kop =>
                {
                    foreach (var tekst in new[] { "Plaats", "Naam", "Totaal", "Verschil\nmet vorige" })
                    {
                        kop.Cell().RowSpan(2).Element(Kop).Text(tekst);
                    }

                    MaandKop(kop, stand, metDatum: true);
                });

                foreach (var regel in stand.Punten)
                {
                    tabel.Cell().Element(c => Cel(c)).Text(Getal(regel.Plaats));
                    tabel.Cell().Element(c => Cel(c, links: true)).Text(regel.Naam);
                    tabel.Cell().Element(c => Cel(c, Kleur(regel.Plaats, kleuren, Rest))).Text(Getal(regel.Totaal)).Bold();
                    tabel.Cell().Element(c => Cel(c, VerschilKleur)).Text(Getal(regel.Verschil)).Bold();
                    PuntenCellen(tabel, stand, regel, kleuren);
                }

                foreach (var invaller in stand.Invallers)
                {
                    tabel.Cell().Element(c => Cel(c));
                    tabel.Cell().Element(c => Cel(c, links: true)).Text($"{invaller.Naam} (invaller)").Italic();
                    tabel.Cell().Element(c => Cel(c));
                    tabel.Cell().Element(c => Cel(c));
                    PuntenCellen(tabel, stand, invaller, kleuren);
                }
            });
            kolom.Item().Element(c => Legenda(c, kleuren));
        });
    }

    private static void PuntenCellen(TableDescriptor tabel, StandWeergave stand, WeergaveRegel regel, Kleuren kleuren)
    {
        for (var i = 0; i < stand.Kolommen.Count; i++)
        {
            var kolom = stand.Kolommen[i];
            var cel = regel.Cel(i);
            if (kolom.IsVrij)
            {
                tabel.Cell().Element(c => Cel(c)).Text(kolom.Vrij).FontSize(7).Italic().FontColor("#46524a");
            }
            else if (cel.IsLeeg)
            {
                tabel.Cell().Element(c => Cel(c));
            }
            else
            {
                var achtergrond = cel.IsAfwezig ? kleuren.Afwezig : Kleur(cel.Plaats, kleuren, "#ffffff");
                tabel.Cell().Element(c => Cel(c, achtergrond)).Text(Getal(cel.Waarde));
            }
        }
    }

    private static void PittenTabel(IContainer container, StandWeergave stand, Kleuren kleuren)
    {
        container.Column(kolom =>
        {
            kolom.Item().PaddingBottom(2).Text($"Pitten {stand.Jaar}").FontSize(12).Bold();
            kolom.Item().Table(tabel =>
            {
                tabel.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(30);
                    c.RelativeColumn(4);
                    c.ConstantColumn(38);
                    for (var i = 0; i < stand.Kolommen.Count; i++)
                    {
                        c.RelativeColumn(1);
                    }
                });

                tabel.Header(kop =>
                {
                    foreach (var tekst in new[] { "Plaats", "Naam", "Totaal" })
                    {
                        kop.Cell().RowSpan(2).Element(Kop).Text(tekst);
                    }

                    MaandKop(kop, stand, metDatum: false);
                });

                foreach (var regel in stand.Pitten)
                {
                    PittenRegel(tabel, stand, regel, kleuren);
                }

                WeekTotalen(tabel, stand);
            });
        });
    }

    private static void PittenRegel(TableDescriptor tabel, StandWeergave stand, WeergaveRegel regel, Kleuren kleuren)
    {
        tabel.Cell().Element(c => Cel(c)).Text(Getal(regel.Plaats));
        tabel.Cell().Element(c => Cel(c, links: true)).Text(regel.Naam);
        tabel.Cell().Element(c => Cel(c, Kleur(regel.Plaats, kleuren, Rest))).Text(Getal(regel.Totaal)).Bold();
        for (var i = 0; i < stand.Kolommen.Count; i++)
        {
            var cel = regel.Cel(i);
            tabel.Cell().Element(c => Cel(c)).Text(cel.IsLeeg ? string.Empty : Getal(cel.Waarde));
        }
    }

    private static void WeekTotalen(TableDescriptor tabel, StandWeergave stand)
    {
        const string Voet = "#f3f6f4";
        tabel.Cell().Element(c => Cel(c, Voet));
        tabel.Cell().Element(c => Cel(c, Voet, links: true)).Text("Week totalen").Bold();
        tabel.Cell().Element(c => Cel(c, Voet));
        foreach (var kolom in stand.Kolommen)
        {
            tabel.Cell().Element(c => Cel(c, Voet)).Text(kolom.IsGespeeld ? Getal(kolom.PittenTotaal) : string.Empty).Bold();
        }
    }

    private static void MaandKop(TableCellDescriptor kop, StandWeergave stand, bool metDatum)
    {
        kop.Cell().ColumnSpan((uint)stand.Kolommen.Count).Element(Kop).Text(Hoofdletter(stand.LaatsteAvond.ToString("MMMM", Nl)));
        foreach (var dag in stand.Kolommen.Select(k => k.Datum))
        {
            var tekst = $"wk {Kalender.Week(dag)}" + (metDatum ? "\n" + dag.ToString("d MMM", Nl).Replace(".", string.Empty, StringComparison.Ordinal) : string.Empty);
            kop.Cell().Element(Kop).Text(tekst);
        }
    }

    private static IContainer Kop(IContainer container)
        => container.Border(0.5f).BorderColor(Lijn).Background(KopKleur).PaddingVertical(1.5f).PaddingHorizontal(2).AlignCenter().AlignMiddle().DefaultTextStyle(s => s.FontSize(7.5f).SemiBold());

    private static IContainer Cel(IContainer container, string achtergrond = "#ffffff", bool links = false)
    {
        var cel = container.Border(0.5f).BorderColor(Lijn).Background(achtergrond).PaddingVertical(1.5f).PaddingHorizontal(3).AlignMiddle();
        return links ? cel.AlignLeft() : cel.AlignCenter();
    }

    private static string Kleur(int plaats, Kleuren kleuren, string anders) => plaats switch
    {
        1 => kleuren.Eerste,
        2 => kleuren.Tweede,
        3 => kleuren.Derde,
        _ => anders,
    };

    private static string Hoofdletter(string tekst) => tekst.Length == 0 ? tekst : char.ToUpper(tekst[0], Nl) + tekst[1..];

    private static string Getal(int getal) => getal.ToString(CultureInfo.InvariantCulture);
}
