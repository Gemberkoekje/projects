using Klaverjas.application.Commands;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Commands;

public sealed class Spelers_specs
{
    private static SpelerAanpassen Aanpassing(Speler speler, bool doetMee, bool inStand)
        => new SpelerAanpassen { Id = speler.Id, Naam = speler.Naam, Soort = speler.Soort, DoetMee = doetMee, InStand = inStand };

    private static SpelerAanpassenHandler Aanpassen(InMemorySpelerStore store)
        => new SpelerAanpassenHandler(store, NullLogger<SpelerAanpassenHandler>.Instance);

    [Test]
    public async Task stopping_takes_a_member_out_of_the_stand_by_default()
    {
        var annie = Lid("Annie de Graaf");
        var store = new InMemorySpelerStore(annie);

        await Aanpassen(store).HandleAsync(Aanpassing(annie, doetMee: false, inStand: true));

        store.Alle.Single().Should().Match<Speler>(s => !s.DoetMee && !s.InStand);
    }

    [Test]
    public async Task a_stopped_member_can_be_kept_in_the_stand_afterwards()
    {
        var annie = Lid("Annie de Graaf") with { DoetMee = false, InStand = false };
        var store = new InMemorySpelerStore(annie);

        await Aanpassen(store).HandleAsync(Aanpassing(annie, doetMee: false, inStand: true));

        store.Alle.Single().InStand.Should().BeTrue();
    }

    [Test]
    public async Task two_players_cannot_share_a_name()
    {
        var annie = Lid("Annie de Graaf");
        var kees = Lid("Kees Bakker");
        var store = new InMemorySpelerStore(annie, kees);

        var result = await Aanpassen(store).HandleAsync(Aanpassing(kees, doetMee: true, inStand: true) with { Naam = "annie de graaf" });

        result.IsValid.Should().BeFalse();
    }

    [Test]
    public async Task a_name_is_required()
    {
        var store = new InMemorySpelerStore();
        var handler = new SpelerToevoegenHandler(store, new InMemoryAvondStore(), NullLogger<SpelerToevoegenHandler>.Instance);

        var result = await handler.HandleAsync(new SpelerToevoegen { Naam = "  ", Soort = Soort.Lid });

        result.IsValid.Should().BeFalse();
        store.Alle.Should().BeEmpty();
    }

    [Test]
    public async Task a_new_member_joins_today_and_hears_how_many_evenings_count_as_absent()
    {
        var store = new InMemorySpelerStore();
        var avonden = new InMemoryAvondStore(Avond(Wk45), Avond(Wk46), Avond(Wk47));
        var handler = new SpelerToevoegenHandler(store, avonden, NullLogger<SpelerToevoegenHandler>.Instance);

        using (Clock.SetTimeForCurrentContext(() => new DateTime(2026, 11, 18, 10, 00, 00, DateTimeKind.Utc)))
        {
            var result = await handler.HandleAsync(new SpelerToevoegen { Naam = "Truus Vermeulen", Soort = Soort.Lid });

            result.Value.GemisteAvonden.Should().Be(3);
            result.Value.Speler.LidVanaf.Should().Be(new DateOnly(2026, 11, 18));
        }
    }

    [Test]
    public async Task a_new_substitute_misses_nothing()
    {
        var handler = new SpelerToevoegenHandler(new InMemorySpelerStore(), new InMemoryAvondStore(Avond(Wk45)), NullLogger<SpelerToevoegenHandler>.Instance);

        var result = await handler.HandleAsync(new SpelerToevoegen { Naam = "Bert van Loon", Soort = Soort.Invaller });

        (result.Value.GemisteAvonden, result.Value.Speler.LidVanaf).Should().Be((0, DateOnly.MinValue));
    }
}
