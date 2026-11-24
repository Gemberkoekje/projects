using Klaverjas.infrastructure.Adapters.Pdf;
using QuestPDF.Fluent;
using System.Text;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Mail;

public sealed class StandPdf_specs
{
    [Test]
    public void the_standings_fit_on_one_landscape_page()
    {
        var spelers = Enumerable.Range(1, 26).Select(i => Lid($"Lid{i:00} Achternaam-Met-Een-Lange-Naam")).ToList();
        var avonden = new[] { Wk45, Wk46, Wk47 }.Select((d, n) => Avond(d, [.. spelers.Select((s, i) => Speelt(s, 4000 + (((i * 37) + (n * 101)) % 900), pitten: (i + n) % 3))])).ToArray();
        var stand = StandBerekening.Bereken(spelers, avonden, Standaard, 2026);
        var online = new OnlineStand { Stand = StandWeergave.Van(stand, Standaard), Bericht = "Tot maandag!", OnlineSinds = DateTime.UtcNow };

        Encoding.Latin1.GetString(new QuestPdfStandMaker().Maak(online, Standaard).ToArray()).Should().StartWith("%PDF-");
        QuestPdfStandMaker.Opbouw(online, Standaard).GenerateImages().Should().ContainSingle();
    }
}
