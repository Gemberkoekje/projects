using Klaverjas.application.Commands;
using Klaverjas.application.Queries;
using Klaverjas.core.Tafels;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Tafels;

public sealed class Voorbereiding_specs
{
    private static readonly DateOnly Wk48 = new DateOnly(2026, 11, 23);

    private readonly Speler annie = Lid("Annie de Graaf");
    private readonly Speler bep = Lid("Bep Willems");
    private readonly Speler corrie = Lid("Corrie Smits");
    private readonly Speler kees = Lid("Kees Bakker");

    private static IReadOnlyList<Speler> Leden(int aantal)
        => [.. Enumerable.Range(1, aantal).Select(i => Lid($"Lid{i:00} Achternaam"))];

    [Test]
    public void sitting_out_is_counted_from_this_seasons_evenings()
    {
        var vorigJaar = new DateOnly(2025, 12, 29);

        var geteld = SlaperKeuze.Tel([Avond(Wk45, Slaper(annie, 4800, 1)), Avond(Wk46, Slaper(annie, 4700, 2), Slaper(bep, 4600, 1)), Avond(vorigJaar, Slaper(bep, 4500, 3))], 2026);

        geteld[annie.Id].Should().Be(new Slaapgeschiedenis { Keer = 2, Laatst = Wk46 });
        geteld[bep.Id].Keer.Should().Be(1);
    }

    [Test]
    public void the_fair_proposal_keeps_the_picks_and_fills_with_who_sat_out_least_then_longest_ago()
    {
        var geschiedenis = new Dictionary<Guid, Slaapgeschiedenis>
        {
            [annie.Id] = new Slaapgeschiedenis { Keer = 1, Laatst = Wk46 },
            [bep.Id] = new Slaapgeschiedenis { Keer = 1, Laatst = Wk45 },
            [corrie.Id] = new Slaapgeschiedenis { Keer = 3, Laatst = Wk45 },
        };

        var voorstel = SlaperKeuze.EerlijkVoorstel([corrie.Id, Guid.Empty], 3, [annie, bep, corrie, kees], geschiedenis);

        voorstel.Should().Equal(corrie.Id, kees.Id, bep.Id);
    }

    [Test]
    public void the_picks_are_cut_or_padded_to_the_number_needed_and_nobody_twice()
    {
        SlaperKeuze.Passend([annie.Id, annie.Id, bep.Id, corrie.Id], 3).Should().Equal(annie.Id, Guid.Empty, bep.Id);
        SlaperKeuze.Passend([annie.Id], 3).Should().Equal(annie.Id, Guid.Empty, Guid.Empty);
    }

    [Test]
    public async Task a_new_preparation_gives_every_active_member_a_card_and_expects_as_many_players()
    {
        var leden = Leden(17).Append(Lid("Gestopt Lid") with { DoetMee = false }).ToArray();
        var handler = new VoorbereidingOphalenHandler(new InMemoryVoorbereidingStore(), new InMemorySpelerStore(leden), new InMemoryAvondStore());

        var formulier = (await handler.HandleAsync(new VoorbereidingOphalen { Datum = Wk48 })).Value;

        (formulier.Voorbereiding.Kaartjes.Count, formulier.Voorbereiding.Verwacht, formulier.Voorbereiding.Blanco, formulier.Schema.Slapers).Should().Be((17, 17, 2, 3));
        formulier.Leden.Should().HaveCount(17);
    }

    [Test]
    public async Task saving_keeps_the_slapers_to_members_with_a_card_and_fills_them_fairly_on_request()
    {
        var leden = Leden(18);
        var voorbereidingen = new InMemoryVoorbereidingStore();
        var handler = new VoorbereidingOpslaanHandler(voorbereidingen, new InMemorySpelerStore([.. leden]), new InMemoryAvondStore(), NullLogger<VoorbereidingOpslaanHandler>.Instance);
        var zonderKaartje = leden[17];

        var result = await handler.HandleAsync(new VoorbereidingOpslaan
        {
            Datum = Wk48,
            Kaartjes = [.. leden.Take(17).Select(s => s.Id)],
            Blanco = 2,
            Verwacht = 17,
            Slapers = [zonderKaartje.Id, leden[3].Id],
            Voorstellen = true,
        });

        result.IsValid.Should().BeTrue();
        var opgeslagen = voorbereidingen.Alle.Single();
        opgeslagen.Slapers.Should().HaveCount(3).And.NotContain(zonderKaartje.Id).And.NotContain(Guid.Empty);
        opgeslagen.Slapers[1].Should().Be(leden[3].Id);
    }

    [Test]
    public async Task the_expected_number_follows_the_ticked_cards_until_dad_sets_it()
    {
        var leden = Leden(20);
        var voorbereidingen = new InMemoryVoorbereidingStore();
        var handler = new VoorbereidingOpslaanHandler(voorbereidingen, new InMemorySpelerStore([.. leden]), new InMemoryAvondStore(), NullLogger<VoorbereidingOpslaanHandler>.Instance);
        VoorbereidingOpslaan Met(int kaartjes, int verwacht) => new VoorbereidingOpslaan { Datum = Wk48, Kaartjes = [.. leden.Take(kaartjes).Select(s => s.Id)], Verwacht = verwacht };

        await handler.HandleAsync(Met(kaartjes: 17, verwacht: 20));
        voorbereidingen.Alle.Single().Verwacht.Should().Be(17);

        await handler.HandleAsync(Met(kaartjes: 17, verwacht: 18));
        await handler.HandleAsync(Met(kaartjes: 16, verwacht: 18));
        voorbereidingen.Alle.Single().Should().Match<Voorbereiding>(v => v.Verwacht == 18 && v.VerwachtZelfGekozen);
    }

    [Test]
    public async Task seven_players_have_no_table_plan()
    {
        var handler = new VoorbereidingOpslaanHandler(new InMemoryVoorbereidingStore(), new InMemorySpelerStore(), new InMemoryAvondStore(), NullLogger<VoorbereidingOpslaanHandler>.Instance);

        (await handler.HandleAsync(new VoorbereidingOpslaan { Datum = Wk48, Verwacht = 7 })).IsValid.Should().BeFalse();
    }

    [Test]
    public async Task a_card_has_the_first_name_or_the_full_name_when_two_share_it_and_a_slaper_gets_number_and_round()
    {
        var annieB = Lid("Annie Bos");
        var leden = Leden(15).Concat([annie, annieB]).ToList();
        var voorbereiding = new Voorbereiding
        {
            Id = Voorbereiding.IdVan(Wk48),
            Datum = Wk48,
            Kaartjes = [.. leden.Select(s => s.Id)],
            Blanco = 1,
            Verwacht = 17,
            Slapers = [leden[0].Id, annie.Id, Guid.Empty],
        };
        var store = new InMemoryVoorbereidingStore(voorbereiding);
        var ophalen = new VoorbereidingOphalenHandler(store, new InMemorySpelerStore([.. leden]), new InMemoryAvondStore());

        var kaartjes = (await new KaartjesOphalenHandler(ophalen, new InMemoryInstellingenStore()).HandleAsync(new KaartjesOphalen { Datum = Wk48 })).Value;

        kaartjes.Lijst.Should().HaveCount(18);
        kaartjes.Lijst.Select(k => k.Naam).Should().Contain(["Annie de Graaf", "Annie Bos", "Lid01"]);
        kaartjes.Lijst.Single(k => k.Naam == "Annie de Graaf").Should().Be(new Kaartje { Naam = "Annie de Graaf", SlaperNummer = 2, RondeStil = 2 });
        kaartjes.Lijst[^1].Should().Be(new Kaartje());
        kaartjes.OpenSlapers.Should().Be(1);
    }

    [Test]
    public async Task the_entry_screen_starts_the_picked_slapers_as_slaper_in_their_round()
    {
        var leden = Leden(17);
        var voorbereiding = new Voorbereiding { Id = Voorbereiding.IdVan(Wk48), Datum = Wk48, Verwacht = 17, Slapers = [leden[4].Id, leden[2].Id, leden[9].Id] };
        var handler = new UitslagOphalenHandler(new InMemoryAvondStore(), new InMemorySpelerStore([.. leden]), new InMemoryInstellingenStore(), new InMemoryVoorbereidingStore(voorbereiding));

        var formulier = (await handler.HandleAsync(new UitslagOphalen { Datum = Wk48 })).Value;

        formulier.Leden.Where(r => r.Status == Status.Slaper).Select(r => (r.Speler.Id, r.Ronde)).Should().BeEquivalentTo([(leden[4].Id, 1), (leden[2].Id, 2), (leden[9].Id, 3)]);
    }
}
