using Klaverjas.application.Queries;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Queries;

public sealed class Queries_specs
{
    private readonly Speler annie = Lid("Annie de Graaf");
    private readonly Speler kees = Lid("Kees Bakker");
    private readonly Speler bert = Invaller("Bert van Loon");

    [Test]
    public async Task a_new_evening_lists_every_active_member_as_playing()
    {
        var gestopt = Lid("Gerrit Kok") with { DoetMee = false };
        var handler = new UitslagOphalenHandler(new InMemoryAvondStore(), new InMemorySpelerStore(kees, annie, gestopt, bert), new InMemoryInstellingenStore());

        var formulier = (await handler.HandleAsync(new UitslagOphalen { Datum = Wk47 })).Value;

        formulier.IsNieuw.Should().BeTrue();
        formulier.Leden.Select(r => (r.Speler.Naam, r.Status)).Should().Equal(("Annie de Graaf", Status.Speelt), ("Kees Bakker", Status.Speelt));
        formulier.AndereInvallers.Should().ContainSingle().Which.Id.Should().Be(bert.Id);
    }

    [Test]
    public async Task an_entered_evening_shows_what_was_saved()
    {
        var avonden = new InMemoryAvondStore(Avond(Wk47, Speelt(annie, 4918, pitten: 1), Afwezig(kees), Speelt(bert, 5000)));
        var handler = new UitslagOphalenHandler(avonden, new InMemorySpelerStore(annie, kees, bert), new InMemoryInstellingenStore());

        var formulier = (await handler.HandleAsync(new UitslagOphalen { Datum = Wk47 })).Value;

        formulier.IsNieuw.Should().BeFalse();
        formulier.Leden.Select(r => (r.Status, r.Punten, r.Pitten)).Should().Equal((Status.Speelt, "4918", "1"), (Status.Afwezig, string.Empty, string.Empty));
        formulier.Invallers.Should().ContainSingle().Which.Punten.Should().Be("5000");
        formulier.AndereInvallers.Should().BeEmpty();
    }

    [Test]
    public async Task an_evening_without_play_has_no_entry_screen()
    {
        var instellingen = new InMemoryInstellingenStore(Standaard with { GeenSpeelavonden = [new GeenSpeelavond { Datum = Wk47, Reden = "Feest" }] });
        var handler = new UitslagOphalenHandler(new InMemoryAvondStore(), new InMemorySpelerStore(annie), instellingen);

        (await handler.HandleAsync(new UitslagOphalen { Datum = Wk47 })).IsValid.Should().BeFalse();
    }

    [Test]
    public async Task the_evenings_list_is_newest_first_with_the_evenings_without_play_among_them()
    {
        var instellingen = new InMemoryInstellingenStore(Standaard with { GeenSpeelavonden = [new GeenSpeelavond { Datum = Wk46, Reden = "Feest" }] });
        var avonden = new InMemoryAvondStore(Avond(Wk45, Speelt(annie, 4000), Slaper(kees, 4500, 1)), Avond(Wk47, Speelt(annie, 4000), Afwezig(kees)));
        var handler = new AvondenOphalenHandler(avonden, instellingen);

        var overzicht = (await handler.HandleAsync(new AvondenOphalen())).Value;

        overzicht.VolgendeAvond.Should().Be(new DateOnly(2026, 11, 23));
        overzicht.Regels.Select(r => (r.Datum, r.Gespeeld, r.Slapers, r.GeenSpeelavond)).Should().Equal(
            (Wk47, 1, 0, string.Empty), (Wk46, 0, 0, "Feest"), (Wk45, 2, 1, string.Empty));
    }
}
