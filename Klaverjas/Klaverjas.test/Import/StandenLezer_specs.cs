using Klaverjas.infrastructure.Adapters.Excel;
using static Klaverjas.test.TestHelpers.StandenWerkboek;

namespace Klaverjas.test.Import;

public sealed class StandenLezer_specs
{
    private static StandenBestand Gelezen(byte[] werkboek)
    {
        var result = new ClosedXmlStandenLezer().Lees(werkboek);
        result.IsValid.Should().BeTrue();
        return result.Value;
    }

    [Test]
    public void reads_the_season_the_evenings_and_every_row_with_its_total()
    {
        var bestand = Gelezen(Maak());

        bestand.Jaar.Should().Be(2026);
        bestand.Avonden.Should().Equal(Wk45, Wk46);
        bestand.Punten.Select(r => (r.Naam, r.Totaal)).Should().Equal(("Annie de Graaf", 8764), ("Joop Verhoeven", 9186), ("Kees Bakker", 8244));
        bestand.Punten[0].Waarden.Select(w => (w.Datum, w.Getal)).Should().Equal((Wk45, 4285), (Wk46, 4479));
    }

    [Test]
    public void text_instead_of_scores_is_an_evening_without_play()
    {
        Gelezen(Maak()).GeenSpeelavonden.Should().ContainSingle().Which.Should().Be(new GeenSpeelavond { Datum = Pasen, Reden = "Pasen" });
    }

    [Test]
    public void the_invallers_row_and_the_week_totals_are_read_apart_from_the_names()
    {
        var bestand = Gelezen(Maak(weekTotaal46: 4));

        bestand.Invallers.Should().ContainSingle().Which.Should().Be(new DatumWaarde { Datum = Wk45, Getal = 5292 });
        bestand.WeekTotalenPitten.Select(w => w.Getal).Should().Equal(1, 4);
        bestand.Pitten.Select(r => r.Naam).Should().Equal("Kees Bakker", "Annie de Graaf", "Joop Verhoven");
    }

    [Test]
    public void totals_are_read_as_the_workbook_last_computed_them()
    {
        Gelezen(Maak(totaalAlsFormule: true)).Punten[0].Totaal.Should().Be(8764);
    }

    [Test]
    public void a_file_that_is_no_workbook_is_refused_with_a_message()
    {
        var result = new ClosedXmlStandenLezer().Lees([1, 2, 3]);

        result.IsValid.Should().BeFalse();
        result.Messages.Single().Message.Should().Contain(".xlsx");
    }

    [Test]
    public void a_workbook_without_both_sheets_is_refused()
    {
        using var werkboek = new ClosedXML.Excel.XLWorkbook();
        werkboek.AddWorksheet("Iets anders");
        using var stroom = new System.IO.MemoryStream();
        werkboek.SaveAs(stroom);

        new ClosedXmlStandenLezer().Lees(stroom.ToArray()).Messages.Single().Message.Should().Contain("\"Stand\" en \"pit\"");
    }
}
