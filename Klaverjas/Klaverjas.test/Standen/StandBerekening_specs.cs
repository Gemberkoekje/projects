using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Standen;

public sealed class StandBerekening_specs
{
    private readonly Speler annie = Lid("Annie de Graaf");
    private readonly Speler kees = Lid("Kees Bakker");
    private readonly Speler joop = Lid("Joop Verhoeven");
    private readonly Speler riet = Lid("Riet Mulder");
    private readonly Speler bert = Invaller("Bert van Loon");

    [Test]
    public void the_total_is_the_sum_of_every_evening_without_dropping_the_lowest()
    {
        var stand = Bereken([annie], Avond(Wk45, Speelt(annie, 4285)), Avond(Wk46, Speelt(annie, 3998)), Avond(Wk47, Speelt(annie, 4918)));

        stand.RegelVan(annie).Totaal.Should().Be(4285 + 3998 + 4918);
    }

    [Test]
    public void ties_share_a_place_and_the_next_place_is_skipped()
    {
        var stand = Bereken([annie, kees, joop, riet], Avond(Wk45, Speelt(annie, 5000), Speelt(kees, 4500), Speelt(joop, 4500), Speelt(riet, 4000)));

        stand.Punten.Select(r => (r.Speler.Naam, r.Plaats)).Should().Equal(
            ("Annie de Graaf", 1), ("Joop Verhoeven", 2), ("Kees Bakker", 2), ("Riet Mulder", 4));
    }

    [Test]
    public void the_gap_is_to_the_row_directly_above_and_zero_for_the_leader()
    {
        var stand = Bereken([annie, kees, joop], Avond(Wk45, Speelt(annie, 5000), Speelt(kees, 4500), Speelt(joop, 4500)));

        stand.Punten.Select(r => r.Verschil).Should().Equal(0, 500, 0);
    }

    [Test]
    public void an_absent_member_gets_the_absence_score_and_no_pits()
    {
        var stand = Bereken([annie], Avond(Wk45, Afwezig(annie)));

        var cel = stand.RegelVan(annie).Op(Wk45);
        (cel.IsAfwezig, cel.Punten, cel.Pitten).Should().Be((true, 4200, 0));
    }

    [Test]
    public void the_absence_score_follows_the_settings()
    {
        var stand = StandBerekening.Bereken([annie], [Avond(Wk45, Afwezig(annie))], new Instellingen { PuntenAfwezig = 4000 }, 2026);

        stand.RegelVan(annie).Totaal.Should().Be(4000);
    }

    [Test]
    public void a_played_4200_is_a_score_not_an_absence()
    {
        var stand = Bereken([annie], Avond(Wk45, Speelt(annie, 4200)));

        stand.RegelVan(annie).Op(Wk45).IsAfwezig.Should().BeFalse();
    }

    [Test]
    public void a_slapers_card_total_counts_as_typed_because_it_already_includes_the_1600()
    {
        var stand = Bereken([annie], Avond(Wk45, Slaper(annie, 4826, ronde: 1)));

        stand.RegelVan(annie).Totaal.Should().Be(4826);
    }

    [Test]
    public void a_substitute_can_take_an_evenings_top_three_but_is_never_ranked()
    {
        var stand = Bereken([annie, kees, bert], Avond(Wk45, Speelt(annie, 4800), Speelt(kees, 4100), Speelt(bert, 5292)));

        stand.Winnaars.Select(w => (w.Speler.Naam, w.Plaats)).Should().Equal(("Bert van Loon", 1), ("Annie de Graaf", 2), ("Kees Bakker", 3));
        stand.Punten.Should().NotContain(r => r.Speler.Id == bert.Id);
        stand.Invallers.Should().ContainSingle(r => r.Speler.Id == bert.Id);
        stand.RegelVan(annie).Op(Wk45).AvondPlaats.Should().Be(2);
    }

    [Test]
    public void absentees_take_no_evening_place()
    {
        var stand = Bereken([annie, kees], Avond(Wk45, Speelt(annie, 3900), Afwezig(kees)));

        stand.Winnaars.Should().ContainSingle().Which.Speler.Id.Should().Be(annie.Id);
        stand.RegelVan(kees).Op(Wk45).AvondPlaats.Should().Be(0);
    }

    [Test]
    public void the_pits_ranking_uses_the_same_tie_rule()
    {
        var stand = Bereken([annie, kees, joop], Avond(Wk45, Speelt(annie, 4000, pitten: 2), Speelt(kees, 4000, pitten: 3), Speelt(joop, 4000, pitten: 2)));

        stand.Pitten.Select(r => (r.Speler.Naam, r.PlaatsPitten)).Should().Equal(
            ("Kees Bakker", 1), ("Annie de Graaf", 2), ("Joop Verhoeven", 2));
    }

    [Test]
    public void week_totals_count_the_members_pits_and_not_the_substitutes()
    {
        var stand = Bereken([annie, kees, bert], Avond(Wk45, Speelt(annie, 4000, pitten: 2), Speelt(kees, 4000, pitten: 1), Speelt(bert, 4000, pitten: 5)));

        stand.WeekTotalenPitten[Wk45].Should().Be(3);
    }

    [Test]
    public void a_member_who_joins_later_counts_as_absent_on_every_evening_before()
    {
        var nieuw = Lid("Truus Vermeulen", lidVanaf: Wk47);

        var stand = Bereken([annie, nieuw], Avond(Wk45, Speelt(annie, 4000)), Avond(Wk46, Speelt(annie, 4000)), Avond(Wk47, Speelt(annie, 4000), Speelt(nieuw, 4600)));

        var regel = stand.RegelVan(nieuw);
        (regel.Op(Wk45).IsAfwezig, regel.Op(Wk46).IsAfwezig, regel.Totaal).Should().Be((true, true, 4200 + 4200 + 4600));
    }

    [Test]
    public void a_member_out_of_the_stand_is_not_ranked_but_their_evening_still_counts_for_the_podium()
    {
        var uit = annie with { DoetMee = false, InStand = false };

        var stand = Bereken([uit, kees], Avond(Wk45, Speelt(uit, 5000), Speelt(kees, 4000)));

        stand.Punten.Should().ContainSingle().Which.Speler.Id.Should().Be(kees.Id);
        stand.Winnaars.Select(w => w.Speler.Id).Should().Equal(uit.Id, kees.Id);
    }

    [Test]
    public void the_lowest_evening_of_each_quarter_is_marked_but_not_subtracted()
    {
        var maart = new DateOnly(2026, 03, 30);

        var stand = Bereken([annie], Avond(maart, Speelt(annie, 3000)), Avond(Wk45, Speelt(annie, 4285)), Avond(Wk46, Speelt(annie, 3998)), Avond(Wk47, Speelt(annie, 3998)));

        var regel = stand.RegelVan(annie);
        regel.Laagste.Select(l => (l.Kwartaal, l.Datum, l.Punten)).Should().Equal((1, maart, 3000), (4, Wk46, 3998));
        regel.Totaal.Should().Be(3000 + 4285 + 3998 + 3998);
    }

    [Test]
    public void a_season_is_a_calendar_year_so_january_starts_at_zero()
    {
        var januari = new DateOnly(2027, 01, 04);

        var stand = StandBerekening.Bereken([annie], [Avond(Wk47, Speelt(annie, 4918)), Avond(januari, Speelt(annie, 4100))], Standaard, 2027);

        (stand.LaatsteAvond, stand.RegelVan(annie).Totaal).Should().Be((januari, 4100));
    }

    [Test]
    public void a_season_without_evenings_is_empty()
    {
        StandBerekening.Bereken([annie], [], Standaard, 2026).IsLeeg.Should().BeTrue();
    }

    [Test]
    public void the_winners_are_of_the_latest_evening_and_ties_share_a_place()
    {
        var stand = Bereken([annie, kees, joop, riet], Avond(Wk45, Speelt(riet, 6000)), Avond(Wk46, Speelt(annie, 5000), Speelt(kees, 5000), Speelt(joop, 4000), Speelt(riet, 3000)));

        stand.Winnaars.Select(w => (w.Speler.Naam, w.Plaats)).Should().Equal(("Annie de Graaf", 1), ("Kees Bakker", 1), ("Joop Verhoeven", 3));
    }
}
