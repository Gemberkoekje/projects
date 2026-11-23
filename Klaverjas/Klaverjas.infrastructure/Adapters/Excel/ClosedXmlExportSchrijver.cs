using ClosedXML.Excel;
using Klaverjas.core.Standen;
using System.Globalization;
using System.IO;

namespace Klaverjas.infrastructure.Adapters.Excel;

/// <summary>
/// Writes every score to one workbook (D17): the latest season's points and pits laid out like Dad's
/// own sheet, every result as one plain list, and the members list.
/// </summary>
public sealed class ClosedXmlExportSchrijver : IExportSchrijver
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <inheritdoc />
    public byte[] Schrijf(ExportGegevens gegevens)
    {
        ArgumentNullException.ThrowIfNull(gegevens);

        using var werkboek = new XLWorkbook();
        var perId = gegevens.Spelers.ToDictionary(s => s.Id);

        if (!gegevens.Stand.IsLeeg)
        {
            StandBlad(werkboek, gegevens.Stand, gegevens.Instellingen);
            PittenBlad(werkboek, gegevens.Stand);
        }

        UitslagenBlad(werkboek, gegevens, perId);
        SpelersBlad(werkboek, gegevens.Spelers);

        using var stroom = new MemoryStream();
        werkboek.SaveAs(stroom);
        return stroom.ToArray();
    }

    private static void StandBlad(XLWorkbook werkboek, Stand stand, Instellingen instellingen)
    {
        var blad = werkboek.AddWorksheet($"Punten {stand.Jaar}");
        Kop(blad, ["Plaats", "Naam", "Totaal", "Verschil met vorige", .. stand.Avonden.Select(Kolomkop)]);

        var rij = 2;
        foreach (var regel in stand.Punten)
        {
            blad.Cell(rij, 1).Value = regel.Plaats;
            blad.Cell(rij, 2).Value = regel.Speler.Naam;
            blad.Cell(rij, 3).Value = regel.Totaal;
            blad.Cell(rij, 4).Value = regel.Verschil;

            for (var i = 0; i < stand.Avonden.Count; i++)
            {
                var cel = regel.Op(stand.Avonden[i]);
                if (cel.IsLeeg)
                {
                    continue;
                }

                var doel = blad.Cell(rij, 5 + i);
                doel.Value = cel.Punten;
                var kleur = cel.IsAfwezig ? instellingen.Kleuren.Afwezig : Kleur(cel.AvondPlaats, instellingen.Kleuren);
                if (kleur.Length > 0)
                {
                    doel.Style.Fill.BackgroundColor = XLColor.FromHtml(kleur);
                }
            }

            rij++;
        }

        blad.Columns().AdjustToContents();
    }

    private static void PittenBlad(XLWorkbook werkboek, Stand stand)
    {
        var blad = werkboek.AddWorksheet($"Pitten {stand.Jaar}");
        Kop(blad, ["Plaats", "Naam", "Totaal", .. stand.Avonden.Select(Kolomkop)]);

        var rij = 2;
        foreach (var regel in stand.Pitten)
        {
            blad.Cell(rij, 1).Value = regel.PlaatsPitten;
            blad.Cell(rij, 2).Value = regel.Speler.Naam;
            blad.Cell(rij, 3).Value = regel.Pitten;

            for (var i = 0; i < stand.Avonden.Count; i++)
            {
                var cel = regel.Op(stand.Avonden[i]);
                if (!cel.IsLeeg)
                {
                    blad.Cell(rij, 4 + i).Value = cel.Pitten;
                }
            }

            rij++;
        }

        blad.Cell(rij, 2).Value = "Week totalen";
        blad.Cell(rij, 2).Style.Font.Bold = true;
        for (var i = 0; i < stand.Avonden.Count; i++)
        {
            blad.Cell(rij, 4 + i).Value = stand.WeekTotalenPitten.TryGetValue(stand.Avonden[i], out var totaal) ? totaal : 0;
        }

        blad.Columns().AdjustToContents();
    }

    private static void UitslagenBlad(XLWorkbook werkboek, ExportGegevens gegevens, Dictionary<Guid, Speler> perId)
    {
        var blad = werkboek.AddWorksheet("Alle uitslagen");
        Kop(blad, ["Datum", "Week", "Naam", "Soort", "Status", "Punten", "Pitten", "Stil in ronde"]);

        var onbekend = new Speler { Naam = "(onbekend)" };
        var regels = gegevens.Avonden
            .OrderBy(a => a.Datum)
            .SelectMany(a => a.Uitslagen
                .Select(u => (Avond: a, Uitslag: u, Speler: perId.TryGetValue(u.SpelerId, out var s) ? s : onbekend))
                .OrderBy(x => x.Speler.Naam, StandBerekening.NaamVolgorde));

        var rij = 2;
        foreach (var (avond, uitslag, speler) in regels)
        {
            Uitslag(blad.Row(rij++), avond.Datum, uitslag, speler, gegevens.Instellingen.PuntenAfwezig);
        }

        blad.Columns().AdjustToContents();
    }

    private static void Uitslag(IXLRow rij, DateOnly datum, Uitslag uitslag, Speler speler, int puntenAfwezig)
    {
        var afwezig = uitslag.Status == Status.Afwezig;

        rij.Cell(1).Value = datum.ToDateTime(TimeOnly.MinValue);
        rij.Cell(1).Style.DateFormat.Format = "d-m-yyyy";
        rij.Cell(2).Value = Kalender.Week(datum);
        rij.Cell(3).Value = speler.Naam;
        rij.Cell(4).Value = speler.IsLid ? "Lid" : "Invaller";
        rij.Cell(5).Value = uitslag.Status.ToString();
        rij.Cell(6).Value = afwezig ? puntenAfwezig : uitslag.Punten;
        rij.Cell(7).Value = afwezig ? 0 : uitslag.Pitten;
        if (uitslag.Ronde > 0)
        {
            rij.Cell(8).Value = uitslag.Ronde;
        }
    }

    private static void SpelersBlad(XLWorkbook werkboek, IReadOnlyList<Speler> spelers)
    {
        var blad = werkboek.AddWorksheet("Spelers");
        Kop(blad, ["Naam", "Soort", "E-mailadres", "Telefoon", "Adres", "Verjaardag", "Doet mee", "In de stand"]);

        var rij = 2;
        foreach (var speler in spelers.OrderBy(s => s.IsLid ? 0 : 1).ThenBy(s => s.Naam, StandBerekening.NaamVolgorde))
        {
            blad.Cell(rij, 1).Value = speler.Naam;
            blad.Cell(rij, 2).Value = speler.IsLid ? "Lid" : "Invaller";
            blad.Cell(rij, 3).Value = speler.Email;
            blad.Cell(rij, 4).Value = speler.Telefoon;
            blad.Cell(rij, 5).Value = speler.Adres;
            blad.Cell(rij, 6).Value = speler.Verjaardag;
            blad.Cell(rij, 7).Value = speler.DoetMee ? "Ja" : "Gestopt";
            blad.Cell(rij, 8).Value = speler.InStand ? "Ja" : "Nee";
            rij++;
        }

        blad.Columns().AdjustToContents();
    }

    private static void Kop(IXLWorksheet blad, IReadOnlyList<string> koppen)
    {
        for (var i = 0; i < koppen.Count; i++)
        {
            blad.Cell(1, i + 1).Value = koppen[i];
        }

        blad.Row(1).Style.Font.Bold = true;
        blad.SheetView.FreezeRows(1);
    }

    private static string Kolomkop(DateOnly datum)
        => $"wk {Kalender.Week(datum).ToString(CultureInfo.InvariantCulture)} {datum.ToString("d MMM", Nl).Replace(".", string.Empty, StringComparison.Ordinal)}";

    private static string Kleur(int plaats, Kleuren kleuren) => plaats switch
    {
        1 => kleuren.Eerste,
        2 => kleuren.Tweede,
        3 => kleuren.Derde,
        _ => string.Empty,
    };
}
