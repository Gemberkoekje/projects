using ClosedXML.Excel;
using System.IO;

namespace Klaverjas.test.TestHelpers;

/// <summary>
/// A small "Standen" workbook in Dad's layout, with invented names: the `Stand` sheet (dates in row 2,
/// "Naam" in row 3, quarter columns in between, a holiday column, next January's column, an
/// "invallers" row) and the `pit` sheet (dates in row 3, one name spelled differently, "Week Totalen").
/// </summary>
internal static class StandenWerkboek
{
    public static readonly DateOnly Wk45 = new DateOnly(2026, 11, 02);
    public static readonly DateOnly Wk46 = new DateOnly(2026, 11, 09);
    public static readonly DateOnly Pasen = new DateOnly(2026, 11, 16);
    public static readonly DateOnly Wk48 = new DateOnly(2026, 11, 23);
    public static readonly DateOnly Januari = new DateOnly(2027, 01, 04);

    /// <summary>Builds the workbook.</summary>
    /// <param name="weekTotaal46">What the sheet's "Week Totalen" says for week 46 (the real sum is 3).</param>
    /// <param name="totaalAlsFormule">Writes the totals as formulas with their computed values, as Excel does.</param>
    /// <returns>The .xlsx file.</returns>
    public static byte[] Maak(int weekTotaal46 = 3, bool totaalAlsFormule = false)
    {
        using var werkboek = new XLWorkbook();

        var stand = werkboek.AddWorksheet("Stand");
        stand.Cell(1, 1).Value = " \"De Samenspelers\" Punten 2026";
        stand.Cell(2, 3).Value = "Totaal";
        stand.Cell(3, 1).Value = "Plaats";
        stand.Cell(3, 2).Value = "Naam";
        stand.Cell(3, 3).Value = "Score";

        // E: wk45, F: wk46, G: "Kwartaal" (a sum column, not a date), H: Pasen, I: wk48 (not played yet), J: next January.
        stand.Cell(2, 5).Value = Datum(Wk45);
        stand.Cell(2, 6).Value = Datum(Wk46);
        stand.Cell(2, 7).Value = "Kwartaal";
        stand.Cell(2, 8).Value = Datum(Pasen);
        stand.Cell(2, 9).Value = Datum(Wk48);
        stand.Cell(2, 10).Value = Datum(Januari);

        Rij(stand, 4, "Annie de Graaf", 4285, 4479, totaalAlsFormule);
        Rij(stand, 5, "Joop Verhoeven", 4200, 4986, totaalAlsFormule);
        Rij(stand, 6, "Kees Bakker", 4199, 4045, totaalAlsFormule);

        stand.Cell(7, 2).Value = "invallers";
        stand.Cell(7, 5).Value = 5292;
        stand.Cell(9, 2).Value = "1e Plaats";

        var pit = werkboek.AddWorksheet("pit");
        pit.Cell(2, 2).Value = " Pitten 2026";
        pit.Cell(3, 2).Value = "Naam";
        pit.Cell(3, 3).Value = "Score";
        pit.Cell(3, 4).Value = Datum(Wk45);
        pit.Cell(3, 5).Value = Datum(Wk46);
        pit.Cell(3, 6).Value = Datum(Wk48);

        PitRij(pit, 4, "Kees Bakker", 1, 1);
        PitRij(pit, 5, "Annie de Graaf", 0, 2);
        PitRij(pit, 6, "Joop Verhoven", 0, 0);
        pit.Cell(7, 2).Value = "Week Totalen";
        pit.Cell(7, 4).Value = 1;
        pit.Cell(7, 5).Value = weekTotaal46;

        using var stroom = new MemoryStream();
        werkboek.SaveAs(stroom, new SaveOptions { EvaluateFormulasBeforeSaving = true });
        return stroom.ToArray();
    }

    private static DateTime Datum(DateOnly datum) => datum.ToDateTime(TimeOnly.MinValue);

    private static void Rij(IXLWorksheet blad, int rij, string naam, int wk45, int wk46, bool formule)
    {
        blad.Cell(rij, 2).Value = naam;
        blad.Cell(rij, 5).Value = wk45;
        blad.Cell(rij, 6).Value = wk46;
        blad.Cell(rij, 7).FormulaA1 = $"SUM(E{rij}:F{rij})";
        blad.Cell(rij, 8).Value = "Pasen";
        blad.Cell(rij, 9).Value = " ";

        if (formule)
        {
            blad.Cell(rij, 3).FormulaA1 = $"SUM(E{rij}:F{rij},I{rij})";
        }
        else
        {
            blad.Cell(rij, 3).Value = wk45 + wk46;
        }
    }

    private static void PitRij(IXLWorksheet blad, int rij, string naam, int wk45, int wk46)
    {
        blad.Cell(rij, 2).Value = naam;
        blad.Cell(rij, 3).Value = wk45 + wk46;
        blad.Cell(rij, 4).Value = wk45;
        blad.Cell(rij, 5).Value = wk46;
    }
}
