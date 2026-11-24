using System.Text.Json;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Standen;

public sealed class StandWeergave_specs
{
    private readonly Speler annie = Lid("Annie de Graaf");
    private readonly Speler kees = Lid("Kees Bakker");
    private readonly Speler bert = Invaller("Bert van Loon");

    private static StandWeergave Weergave(IEnumerable<Speler> spelers, Instellingen instellingen, params Avond[] avonden)
        => StandWeergave.Van(StandBerekening.Bereken(spelers, avonden, instellingen, 2026), instellingen);

    [Test]
    public void the_columns_are_the_club_days_of_the_latest_evenings_month()
    {
        var instellingen = Standaard with { GeenSpeelavonden = [new GeenSpeelavond { Datum = Wk46, Reden = "Feest" }] };

        var weergave = Weergave([annie, kees], instellingen, Avond(Wk45, Speelt(annie, 4000), Speelt(kees, 4100)), Avond(Wk47, Speelt(annie, 4500), Afwezig(kees)));

        weergave.Kolommen.Select(k => k.Datum.Day).Should().Equal(2, 9, 16, 23, 30);
        weergave.Kolommen.Select(k => (k.Vrij, k.IsGespeeld)).Should().Equal(
            (string.Empty, true), ("Feest", false), (string.Empty, true), (string.Empty, false), (string.Empty, false));
        weergave.LaatsteKolom.Should().Be(2);
    }

    [Test]
    public void an_evening_on_another_weekday_gets_its_own_column()
    {
        var dinsdag = new DateOnly(2026, 11, 17);

        var weergave = Weergave([annie], Standaard, Avond(Wk47, Speelt(annie, 4000)), Avond(dinsdag, Speelt(annie, 4100)));

        weergave.Kolommen.Select(k => k.Datum.Day).Should().Equal(2, 9, 16, 17, 23, 30);
        weergave.Kolommen[weergave.LaatsteKolom].Datum.Should().Be(dinsdag);
    }

    [Test]
    public void the_cells_carry_the_points_the_place_and_the_absence_score()
    {
        var weergave = Weergave([annie, kees], Standaard, Avond(Wk47, Speelt(annie, 4800, pitten: 2), Afwezig(kees)));
        var kolom = weergave.LaatsteKolom;

        var annieRij = weergave.Punten.Single(r => r.Naam == "Annie de Graaf");
        annieRij.Cel(kolom).Should().Match<WeergaveCel>(c => c.Status == Status.Speelt && c.Waarde == 4800 && c.Plaats == 1);
        weergave.Punten.Single(r => r.Naam == "Kees Bakker").Cel(kolom).Should().Match<WeergaveCel>(c => c.IsAfwezig && c.Waarde == Standaard.PuntenAfwezig);
        weergave.Pitten.Single(r => r.Naam == "Annie de Graaf").Cel(kolom).Waarde.Should().Be(2);
        weergave.Kolommen[kolom].PittenTotaal.Should().Be(2);
    }

    [Test]
    public void a_substitute_is_listed_only_in_a_month_they_played()
    {
        var oktober = new DateOnly(2026, 10, 26);

        var weergave = Weergave([annie, bert], Standaard, Avond(oktober, Speelt(annie, 4000), Speelt(bert, 5000)), Avond(Wk47, Speelt(annie, 4000)));

        weergave.Invallers.Should().BeEmpty();
        weergave.Punten.Should().ContainSingle().Which.Naam.Should().Be("Annie de Graaf");
    }

    [Test]
    public void a_substitute_who_won_is_marked_as_such()
    {
        var weergave = Weergave([annie, bert], Standaard, Avond(Wk47, Speelt(annie, 4000), Speelt(bert, 5000)));

        weergave.Winnaars.First().Should().Match<WeergaveWinnaar>(w => w.Naam == "Bert van Loon" && w.Plaats == 1 && w.IsInvaller);
        weergave.Invallers.Should().ContainSingle().Which.Plaats.Should().Be(0);
    }

    [Test]
    public void only_names_and_numbers_go_online()
    {
        var metGegevens = annie with { Email = "annie@example.org", Telefoon = "06-12345678", Adres = "Dorpsstraat 1", Verjaardag = "12 maart" };

        var json = JsonSerializer.Serialize(Weergave([metGegevens], Standaard, Avond(Wk47, Speelt(metGegevens, 4000))));

        json.Should().Contain("Annie de Graaf");
        json.Should().NotContainAny("example.org", "06-12345678", "Dorpsstraat", "maart");
    }

    [Test]
    public void a_season_without_evenings_shows_nothing()
    {
        var weergave = Weergave([annie], Standaard);

        weergave.IsLeeg.Should().BeTrue();
        weergave.Kolommen.Should().BeEmpty();
        weergave.LaatsteKolom.Should().Be(-1);
    }
}
