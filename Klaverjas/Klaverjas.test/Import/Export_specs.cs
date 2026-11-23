using ClosedXML.Excel;
using Klaverjas.infrastructure.Adapters.Excel;
using System.IO;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Import;

public sealed class Export_specs
{
    [Test]
    public void the_export_holds_the_standings_every_result_and_the_players()
    {
        var annie = Lid("Annie de Graaf") with { Email = "annie@voorbeeld.nl" };
        var kees = Lid("Kees Bakker");
        var avonden = new[] { Avond(Wk45, Speelt(annie, 4285, pitten: 1), Afwezig(kees)), Avond(Wk46, Speelt(annie, 4479), Slaper(kees, 4826, ronde: 2)) };

        var bytes = new ClosedXmlExportSchrijver().Schrijf(new ExportGegevens
        {
            Instellingen = Standaard,
            Spelers = [annie, kees],
            Avonden = avonden,
            Stand = Bereken([annie, kees], avonden),
        });

        using var stroom = new MemoryStream(bytes);
        using var werkboek = new XLWorkbook(stroom);
        werkboek.Worksheets.Select(w => w.Name).Should().Equal("Punten 2026", "Pitten 2026", "Alle uitslagen", "Spelers");

        var punten = werkboek.Worksheet("Punten 2026");
        (punten.Cell(2, 2).GetString(), punten.Cell(2, 3).GetValue<int>(), punten.Cell(2, 5).GetValue<int>(), punten.Cell(3, 5).GetValue<int>()).Should().Be(("Kees Bakker", 9026, 4200, 4285));

        var uitslagen = werkboek.Worksheet("Alle uitslagen");
        (uitslagen.Cell(5, 1).IsEmpty(), uitslagen.Cell(6, 1).IsEmpty()).Should().Be((false, true));
        (uitslagen.Cell(5, 3).GetString(), uitslagen.Cell(5, 5).GetString(), uitslagen.Cell(5, 8).GetValue<int>()).Should().Be(("Kees Bakker", "Slaper", 2));

        werkboek.Worksheet("Spelers").Cell(2, 3).GetString().Should().Be("annie@voorbeeld.nl");
    }
}
