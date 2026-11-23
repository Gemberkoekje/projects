using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using System.IO;

namespace Klaverjas.infrastructure.Adapters.Excel;

/// <summary>
/// Reads Dad's "Standen" workbook. It finds its way by the sheet's own labels ("Naam", the row of
/// dates, "invallers", "Week totalen") rather than fixed cell addresses, so a row or column more or
/// less from one year to the next does not break it. Totals are read as the workbook last computed
/// them, never recalculated.
/// </summary>
public sealed class ClosedXmlStandenLezer : IStandenLezer
{
    /// <inheritdoc />
    public Result<StandenBestand> Lees(byte[] inhoud)
    {
        ArgumentNullException.ThrowIfNull(inhoud);

        try
        {
            using var stroom = new MemoryStream(inhoud, writable: false);
            using var werkboek = new XLWorkbook(stroom);
            return Lees(werkboek);
        }
        catch (Exception fout) when (fout is InvalidDataException or IOException or OpenXmlPackageException or ArgumentException or FormatException or NotSupportedException)
        {
            return Result.WithMessages<StandenBestand>(ValidationMessage.Error("Dit bestand kan ik niet lezen. Kies het Excel-bestand (.xlsx) met de standen."));
        }
    }

    private static Result<StandenBestand> Lees(XLWorkbook werkboek)
    {
        var standBlad = ZoekBlad(werkboek, "Stand");
        var pitBlad = ZoekBlad(werkboek, "pit");

        if (standBlad is null || pitBlad is null)
        {
            return Result.WithMessages<StandenBestand>(ValidationMessage.Error(
                "Dit lijkt niet op de Excel met de standen: de bladen \"Stand\" en \"pit\" moeten er allebei in staan."));
        }

        var stand = GelezenBlad.Uit(standBlad, "invallers");
        var pit = GelezenBlad.Uit(pitBlad, "week totalen");

        if (stand.Rijen.Count == 0 || stand.Datums.Count == 0)
        {
            return Result.WithMessages<StandenBestand>(ValidationMessage.Error("Op het blad \"Stand\" vind ik geen namen met scores."));
        }

        // The season is the year most date columns are in; the sheet also has next January's first column.
        var jaar = stand.Datums.Values.GroupBy(d => d.Year).OrderByDescending(g => g.Count()).First().Key;
        var inJaar = stand.Datums.Values.Where(d => d.Year == jaar).ToHashSet();

        var avonden = stand.Rijen.SelectMany(r => r.Waarden).Select(w => w.Datum).Where(inJaar.Contains).Distinct().Order().ToList();
        var vrij = stand.Teksten
            .Where(t => inJaar.Contains(t.Key) && !avonden.Contains(t.Key))
            .Select(t => new GeenSpeelavond { Datum = t.Key, Reden = t.Value })
            .OrderBy(g => g.Datum)
            .ToList();

        return Result.For(new StandenBestand
        {
            Jaar = jaar,
            Avonden = avonden,
            GeenSpeelavonden = vrij,
            Punten = [.. stand.Rijen.Select(r => r with { Waarden = [.. r.Waarden.Where(w => inJaar.Contains(w.Datum))] })],
            Pitten = [.. pit.Rijen],
            WeekTotalenPitten = pit.Bijzonder,
            Invallers = [.. stand.Bijzonder.Where(w => inJaar.Contains(w.Datum))],
        });
    }

    private static IXLWorksheet ZoekBlad(XLWorkbook werkboek, string naam)
        => werkboek.Worksheets.FirstOrDefault(w => string.Equals(w.Name.Trim(), naam, StringComparison.OrdinalIgnoreCase));

    /// <summary>One sheet as read: its date columns, its name rows, and the one labelled row under them.</summary>
    private sealed record GelezenBlad
    {
        public IReadOnlyDictionary<int, DateOnly> Datums { get; init; } = new Dictionary<int, DateOnly>();

        public IReadOnlyList<BestandsRegel> Rijen { get; init; } = [];

        /// <summary>The "invallers" or "Week totalen" row.</summary>
        public IReadOnlyList<DatumWaarde> Bijzonder { get; init; } = [];

        /// <summary>Per date, the text typed in the name rows instead of a score ("Pasen").</summary>
        public IReadOnlyDictionary<DateOnly, string> Teksten { get; init; } = new Dictionary<DateOnly, string>();

        public static GelezenBlad Uit(IXLWorksheet blad, string bijzonderLabel)
        {
            var laatsteRij = blad.LastRowUsed()?.RowNumber() ?? 0;
            var laatsteKolom = blad.LastColumnUsed()?.ColumnNumber() ?? 0;

            // The header row is the one with "Naam"; the totals are in the column right of it.
            var (naamRij, naamKolom) = ZoekNaam(blad, Math.Min(laatsteRij, 10), laatsteKolom);
            if (naamRij == 0)
            {
                return new GelezenBlad();
            }

            var datums = ZoekDatums(blad, naamRij, laatsteKolom);
            var rijen = new List<BestandsRegel>();
            var bijzonder = new List<DatumWaarde>();
            var teksten = new Dictionary<DateOnly, List<string>>();

            for (var r = naamRij + 1; r <= laatsteRij; r++)
            {
                var naam = Tekst(Waarde(blad.Cell(r, naamKolom)));
                if (naam.Length == 0)
                {
                    break;
                }

                var waarden = new List<DatumWaarde>();
                foreach (var (kolom, datum) in datums)
                {
                    var cel = Waarde(blad.Cell(r, kolom));
                    if (cel.IsNumber)
                    {
                        waarden.Add(new DatumWaarde { Datum = datum, Getal = (int)Math.Round(cel.GetNumber()) });
                    }
                    else if (Tekst(cel).Length > 0)
                    {
                        Voeg(teksten, datum, Tekst(cel));
                    }
                }

                if (naam.StartsWith(bijzonderLabel, StringComparison.OrdinalIgnoreCase))
                {
                    bijzonder = waarden;
                    break;
                }

                var totaal = Waarde(blad.Cell(r, naamKolom + 1));
                rijen.Add(new BestandsRegel { Naam = naam, Waarden = waarden, Totaal = totaal.IsNumber ? (int)Math.Round(totaal.GetNumber()) : 0 });
            }

            return new GelezenBlad
            {
                Datums = datums,
                Rijen = rijen,
                Bijzonder = bijzonder,
                Teksten = teksten.ToDictionary(t => t.Key, t => t.Value.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key),
            };
        }

        private static (int Rij, int Kolom) ZoekNaam(IXLWorksheet blad, int totRij, int totKolom)
        {
            for (var r = 1; r <= totRij; r++)
            {
                for (var k = 1; k <= totKolom; k++)
                {
                    if (string.Equals(Tekst(Waarde(blad.Cell(r, k))), "Naam", StringComparison.OrdinalIgnoreCase))
                    {
                        return (r, k);
                    }
                }
            }

            return (0, 0);
        }

        /// <summary>The row at or above the header with the most dates: those are the evening columns.</summary>
        private static Dictionary<int, DateOnly> ZoekDatums(IXLWorksheet blad, int totRij, int totKolom)
        {
            var beste = new Dictionary<int, DateOnly>();

            for (var r = 1; r <= totRij; r++)
            {
                var rij = new Dictionary<int, DateOnly>();
                for (var k = 1; k <= totKolom; k++)
                {
                    var cel = Waarde(blad.Cell(r, k));
                    if (cel.IsDateTime)
                    {
                        rij[k] = DateOnly.FromDateTime(cel.GetDateTime());
                    }
                }

                if (rij.Count > beste.Count)
                {
                    beste = rij;
                }
            }

            return beste;
        }

        private static void Voeg(Dictionary<DateOnly, List<string>> teksten, DateOnly datum, string tekst)
        {
            if (!teksten.TryGetValue(datum, out var lijst))
            {
                lijst = [];
                teksten[datum] = lijst;
            }

            lijst.Add(tekst);
        }

        /// <summary>A cell's value as the workbook last saved it: a formula is never recalculated here.</summary>
        private static XLCellValue Waarde(IXLCell cel) => cel.HasFormula ? cel.CachedValue : cel.Value;

        private static string Tekst(XLCellValue waarde)
            => waarde.IsText ? string.Join(' ', waarde.GetText().Split((char[])null, StringSplitOptions.RemoveEmptyEntries)) : string.Empty;
    }
}
