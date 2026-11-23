using Klaverjas.application.Commands;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Import;

public sealed class ImportPlanner_specs
{
    private static readonly DateTime Nu = new DateTime(2026, 11, 23, 12, 00, 00, DateTimeKind.Utc);

    private static BestandsRegel Rij(string naam, int totaal, params (DateOnly Datum, int Getal)[] waarden)
        => new BestandsRegel { Naam = naam, Totaal = totaal, Waarden = [.. waarden.Select(w => new DatumWaarde { Datum = w.Datum, Getal = w.Getal })] };

    private static StandenBestand Bestand(int weekTotaal46 = 3) => new StandenBestand
    {
        Jaar = 2026,
        Avonden = [Wk45, Wk46],
        GeenSpeelavonden = [new GeenSpeelavond { Datum = Wk47, Reden = "Pasen" }],
        Punten =
        [
            Rij("Annie de Graaf", 8764, (Wk45, 4285), (Wk46, 4479)),
            Rij("Joop Verhoeven", 9186, (Wk45, 4200), (Wk46, 4986)),
            Rij("Kees Bakker", 8244, (Wk45, 4199), (Wk46, 4045)),
        ],
        Pitten =
        [
            Rij("Kees Bakker", 2, (Wk45, 1), (Wk46, 1)),
            Rij("Annie de Graaf", 2, (Wk45, 0), (Wk46, 2)),
            Rij("Joop Verhoven", 0, (Wk45, 0), (Wk46, 0)),
        ],
        WeekTotalenPitten = [new DatumWaarde { Datum = Wk45, Getal = 1 }, new DatumWaarde { Datum = Wk46, Getal = weekTotaal46 }],
        Invallers = [new DatumWaarde { Datum = Wk45, Getal = 5292 }],
    };

    private static ImportVoorstel Plan(StandenBestand bestand) => Plan(bestand, [], []);

    private static ImportVoorstel Plan(StandenBestand bestand, IReadOnlyCollection<Speler> spelers, IReadOnlyCollection<Avond> avonden)
        => ImportPlanner.Maak(bestand, spelers, avonden, Standaard, "Standen week 46.xlsx", Guid.NewGuid(), Nu);

    [Test]
    public void the_same_name_couples_exactly_and_a_name_a_letter_off_looks_alike()
    {
        var plan = Plan(Bestand());

        plan.Spelers.Select(s => (s.Naam, s.PitNaam, s.Koppeling)).Should().Equal(
            ("Annie de Graaf", "Annie de Graaf", Koppeling.Precies),
            ("Joop Verhoeven", "Joop Verhoven", Koppeling.LijktOp),
            ("Kees Bakker", "Kees Bakker", Koppeling.Precies));
        plan.LossePitNamen.Should().BeEmpty();
    }

    [Test]
    public void a_name_unlike_any_other_stays_uncoupled()
    {
        var bestand = Bestand() with { Pitten = [Rij("Kees Bakker", 2), Rij("Annie de Graaf", 2), Rij("Gerrit Kok", 0)] };

        var plan = Plan(bestand);

        plan.Spelers.Single(s => s.Naam == "Joop Verhoeven").Koppeling.Should().Be(Koppeling.None);
        plan.LossePitNamen.Should().Equal("Gerrit Kok");
    }

    [Test]
    public void exactly_the_absence_score_becomes_absent_and_4199_stays_a_score()
    {
        var plan = Plan(Bestand());
        var avond = ImportPlanner.Toepassen(plan, Standaard).Avonden.Single(a => a.Datum == Wk45);
        var id = plan.Spelers.ToDictionary(s => s.Naam, s => s.SpelerId);

        avond.Uitslagen.Single(u => u.SpelerId == id["Joop Verhoeven"]).Status.Should().Be(Status.Afwezig);
        avond.Uitslagen.Single(u => u.SpelerId == id["Kees Bakker"]).Should().Match<Uitslag>(u => u.Status == Status.Speelt && u.Punten == 4199);
    }

    [Test]
    public void a_4200_with_pits_was_played()
    {
        var bestand = Bestand() with { Pitten = [Rij("Kees Bakker", 2, (Wk45, 1), (Wk46, 1)), Rij("Annie de Graaf", 2, (Wk46, 2)), Rij("Joop Verhoeven", 2, (Wk45, 2))] };
        var plan = Plan(bestand);
        var joop = plan.Spelers.Single(s => s.Naam == "Joop Verhoeven").SpelerId;

        var uitslag = ImportPlanner.Toepassen(plan, Standaard).Avonden.Single(a => a.Datum == Wk45).Uitslagen.Single(u => u.SpelerId == joop);

        uitslag.Should().Be(new Uitslag { SpelerId = joop, Status = Status.Speelt, Punten = 4200, Pitten = 2 });
    }

    [Test]
    public void pits_on_an_evening_without_play_are_listed_once_and_not_imported()
    {
        var bestand = Bestand() with
        {
            Pitten = [Rij("Kees Bakker", 5, (Wk45, 1), (Wk46, 1), (Wk47, 3)), Rij("Annie de Graaf", 3, (Wk46, 2), (Wk47, 1)), Rij("Joop Verhoven", 0)],
            WeekTotalenPitten = [],
        };

        var plan = Plan(bestand);

        plan.Verschillen.Where(v => v.Soort == VerschilSoort.PittenZonderAvond).Should().ContainSingle()
            .Which.Should().Be(new Verschil { Soort = VerschilSoort.PittenZonderAvond, Datum = Wk47, Excel = 4 });
        plan.Verschillen.Where(v => v.Soort == VerschilSoort.TotaalPitten).Select(v => (v.Wie, v.Excel, v.Programma)).Should().Equal(("Annie de Graaf", 3, 2), ("Kees Bakker", 5, 2));
    }

    [Test]
    public void pits_come_from_the_coupled_row()
    {
        var plan = Plan(Bestand());
        var avond = ImportPlanner.Toepassen(plan, Standaard).Avonden.Single(a => a.Datum == Wk46);
        var annie = plan.Spelers.Single(s => s.Naam == "Annie de Graaf").SpelerId;

        avond.Uitslagen.Single(u => u.SpelerId == annie).Pitten.Should().Be(2);
    }

    [Test]
    public void a_player_already_in_the_app_is_reused_by_name()
    {
        var bestaand = Lid("annie  de graaf");

        var plan = Plan(Bestand(), [bestaand], []);

        plan.Spelers.Single(s => s.Naam == "Annie de Graaf").Should().Match<ImportSpeler>(s => s.SpelerId == bestaand.Id && !s.IsNieuw);
        ImportPlanner.Toepassen(plan, Standaard).NieuweSpelers.Select(s => s.Naam).Should().Equal("Joop Verhoeven", "Kees Bakker", ImportPlanner.InvallerNaam);
    }

    [Test]
    public void the_unnamed_invallers_row_becomes_one_substitute()
    {
        var plan = Plan(Bestand());
        var resultaat = ImportPlanner.Toepassen(plan, Standaard);

        resultaat.NieuweSpelers.Single(s => s.Id == plan.InvallerId).Soort.Should().Be(Soort.Invaller);
        resultaat.Avonden.Single(a => a.Datum == Wk45).Uitslagen.Single(u => u.SpelerId == plan.InvallerId).Punten.Should().Be(5292);
    }

    [Test]
    public void totals_that_add_up_give_no_differences()
    {
        Plan(Bestand()).Verschillen.Should().BeEmpty();
    }

    [Test]
    public void a_week_total_the_sheet_got_wrong_is_listed()
    {
        var plan = Plan(Bestand(weekTotaal46: 2));

        plan.Verschillen.Should().ContainSingle().Which.Should().Be(new Verschil { Soort = VerschilSoort.WeekTotaalPitten, Datum = Wk46, Excel = 2, Programma = 3 });
    }

    [Test]
    public void a_total_that_does_not_add_up_is_listed_with_both_numbers()
    {
        var bestand = Bestand() with { Punten = [Rij("Annie de Graaf", 9000, (Wk45, 4285), (Wk46, 4479)), .. Bestand().Punten.Skip(1)] };

        Plan(bestand).Verschillen.Should().ContainSingle().Which.Should().Be(new Verschil { Soort = VerschilSoort.TotaalPunten, Wie = "Annie de Graaf", Excel = 9000, Programma = 8764 });
    }

    [Test]
    public void coupling_by_hand_moves_a_pits_row_and_recomputes_the_differences()
    {
        var plan = Plan(Bestand());

        var nieuw = ImportPlanner.Herkoppel(plan, "Kees Bakker", "Annie de Graaf", [], Standaard);

        nieuw.Spelers.Select(s => (s.PitNaam, s.Koppeling)).Should().Equal((string.Empty, Koppeling.None), ("Joop Verhoven", Koppeling.LijktOp), ("Annie de Graaf", Koppeling.Handmatig));
        nieuw.LossePitNamen.Should().Equal("Kees Bakker");
        nieuw.Verschillen.Should().Contain(v => v.Soort == VerschilSoort.WeekTotaalPitten && v.Datum == Wk45);
    }

    [Test]
    public void applying_adds_the_evenings_without_play_and_sets_the_first_evening()
    {
        var instellingen = Standaard with { GeenSpeelavonden = [new GeenSpeelavond { Datum = Wk47, Reden = "Al bekend" }] };

        var resultaat = ImportPlanner.Toepassen(Plan(Bestand()), instellingen);

        resultaat.Instellingen.GeenSpeelavonden.Should().ContainSingle().Which.Reden.Should().Be("Al bekend");
        resultaat.Instellingen.EersteAvond.Should().Be(Wk45);
    }

    [Test]
    public void evenings_already_in_the_app_are_counted_as_replaced()
    {
        Plan(Bestand(), [], [Avond(Wk46), Avond(Wk47)]).VervangenAvonden.Should().Be(1);
    }

    [Test]
    public async Task importing_writes_the_players_the_evenings_and_the_settings_and_forgets_the_plan()
    {
        var voorstellen = new InMemoryImportVoorstelStore();
        var spelers = new InMemorySpelerStore();
        var avonden = new InMemoryAvondStore();
        var instellingen = new InMemoryInstellingenStore();
        var plan = Plan(Bestand());
        await voorstellen.OpslaanAsync(plan);

        var result = await new ImportUitvoerenHandler(voorstellen, spelers, avonden, instellingen, NullLogger<ImportUitvoerenHandler>.Instance)
            .HandleAsync(new ImportUitvoeren { Id = plan.Id });

        result.Value.Should().Be(new ImportUitgevoerd { NieuweSpelers = 4, Avonden = 2, GeenSpeelavonden = 1 });
        (spelers.Alle.Count, avonden.Alle.Count, instellingen.Huidig.GeenSpeelavonden.Count, voorstellen.Alle.Count).Should().Be((4, 2, 1, 0));

        var stand = StandBerekening.Bereken(spelers.Alle, avonden.Alle, instellingen.Huidig, 2026);
        stand.Punten.Select(r => r.Totaal).Should().Equal(9186, 8764, 8244);
    }

    [Test]
    public async Task an_applied_plan_cannot_be_applied_twice()
    {
        var handler = new ImportUitvoerenHandler(new InMemoryImportVoorstelStore(), new InMemorySpelerStore(), new InMemoryAvondStore(), new InMemoryInstellingenStore(), NullLogger<ImportUitvoerenHandler>.Instance);

        (await handler.HandleAsync(new ImportUitvoeren { Id = Guid.NewGuid() })).IsValid.Should().BeFalse();
    }
}
