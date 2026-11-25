using Klaverjas.application.Commands;
using Klaverjas.application.Queries;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Commands;

public sealed class Invallers_specs
{
    private readonly Speler annie = Lid("Annie de Graaf");
    private readonly Speler kees = Lid("Kees Bakker");
    private readonly Speler bert = Invaller("Bert van Loon");

    [Test]
    public void a_member_made_substitute_shows_only_on_the_evenings_played()
    {
        // Kees was a member, absent on 9 November; now he is a substitute (Dad, 2026-11-25).
        var invaller = kees with { Soort = Soort.Invaller };
        var stand = Bereken([annie, invaller], Avond(Wk46, Speelt(annie, 4000), Afwezig(invaller)), Avond(Wk47, Speelt(annie, 4100), Speelt(invaller, 4500)));

        var regel = stand.Invallers.Should().ContainSingle().Which;
        regel.Op(Wk46).IsLeeg.Should().BeTrue();
        regel.Op(Wk47).Punten.Should().Be(4500);
        stand.Punten.Should().ContainSingle().Which.Speler.Id.Should().Be(annie.Id);
    }

    [Test]
    public void a_substitute_who_only_has_absences_is_not_in_the_standings()
    {
        var invaller = kees with { Soort = Soort.Invaller };
        var stand = Bereken([annie, invaller], Avond(Wk46, Speelt(annie, 4000), Afwezig(invaller)));

        stand.Invallers.Should().BeEmpty();
        StandWeergave.Van(stand, Standaard).Invallers.Should().BeEmpty();
    }

    [Test]
    public async Task the_entry_screen_leaves_out_a_substitutes_absence()
    {
        var invaller = kees with { Soort = Soort.Invaller };
        var handler = new UitslagOphalenHandler(new InMemoryAvondStore(Avond(Wk46, Speelt(annie, 4000), Afwezig(invaller))), new InMemorySpelerStore(annie, invaller), new InMemoryInstellingenStore(), new InMemoryVoorbereidingStore());

        var formulier = (await handler.HandleAsync(new UitslagOphalen { Datum = Wk46 })).Value;

        formulier.Invallers.Should().BeEmpty();
        formulier.AndereInvallers.Should().ContainSingle().Which.Id.Should().Be(invaller.Id);
    }

    [Test]
    public async Task removing_a_substitute_takes_them_out_of_every_evening()
    {
        var spelers = new InMemorySpelerStore(annie, bert);
        var avonden = new InMemoryAvondStore(Avond(Wk45, Speelt(annie, 4000), Speelt(bert, 5292)), Avond(Wk46, Speelt(annie, 4100)), Avond(Wk47, Speelt(annie, 4200), Speelt(bert, 4800)));

        var result = await new SpelerWeghalenHandler(spelers, avonden, NullLogger<SpelerWeghalenHandler>.Instance).HandleAsync(new SpelerWeghalen { Id = bert.Id });

        result.Value.Should().Be(2);
        spelers.Alle.Should().ContainSingle().Which.Id.Should().Be(annie.Id);
        avonden.Alle.SelectMany(a => a.Uitslagen).Should().OnlyContain(u => u.SpelerId == annie.Id);
    }

    [Test]
    public async Task a_member_is_never_removed()
    {
        var spelers = new InMemorySpelerStore(annie);
        var avonden = new InMemoryAvondStore(Avond(Wk47, Speelt(annie, 4000)));

        var result = await new SpelerWeghalenHandler(spelers, avonden, NullLogger<SpelerWeghalenHandler>.Instance).HandleAsync(new SpelerWeghalen { Id = annie.Id });

        result.IsValid.Should().BeFalse();
        spelers.Alle.Should().ContainSingle();
        avonden.Alle.Single().Uitslagen.Should().ContainSingle();
    }
}
