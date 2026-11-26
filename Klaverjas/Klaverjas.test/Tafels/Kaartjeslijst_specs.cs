using Klaverjas.application.Commands;
using Klaverjas.application.Queries;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Tafels;

/// <summary>"Wie krijgt een kaartje?": members and substitutes, following the players as they are now (Dad, 2026-11-26).</summary>
public sealed class Kaartjeslijst_specs
{
    private static readonly DateOnly Wk48 = new DateOnly(2026, 11, 23);

    private readonly Speler annie = Lid("Annie de Graaf");
    private readonly Speler kees = Lid("Kees Bakker");
    private readonly Speler riet = Lid("Riet Mulder");
    private readonly Speler bert = Invaller("Bert van Loon");

    private static Voorbereiding Bewaard(params Speler[] metKaartje)
        => new Voorbereiding { Id = Voorbereiding.IdVan(Wk48), Datum = Wk48, Kaartjes = [.. metKaartje.Select(s => s.Id)], Verwacht = 4 };

    private static async Task<VoorbereidingFormulier> Scherm(InMemoryVoorbereidingStore store, params Speler[] spelers)
        => (await new VoorbereidingOphalenHandler(store, new InMemorySpelerStore(spelers), new InMemoryAvondStore()).HandleAsync(new VoorbereidingOphalen { Datum = Wk48 })).Value;

    [Test]
    public async Task substitutes_are_in_the_list_but_get_no_card_until_ticked()
    {
        var formulier = await Scherm(new InMemoryVoorbereidingStore(), annie, kees, riet, bert);

        formulier.Invallers.Should().ContainSingle().Which.Id.Should().Be(bert.Id);
        formulier.Voorbereiding.Kaartjes.Should().BeEquivalentTo([annie.Id, kees.Id, riet.Id]);
    }

    [Test]
    public async Task a_member_since_the_preparation_was_saved_gets_a_card()
    {
        // The evening was prepared when Riet was a substitute (or not there yet); now she is a member.
        var formulier = await Scherm(new InMemoryVoorbereidingStore(Bewaard(annie, kees)), annie, kees, riet);

        formulier.Voorbereiding.Kaartjes.Should().Contain(riet.Id).And.HaveCount(3);
    }

    [Test]
    public async Task a_member_dad_unticked_stays_without_a_card()
    {
        var store = new InMemoryVoorbereidingStore();
        var spelers = new InMemorySpelerStore(annie, kees, riet, bert);
        await new VoorbereidingOpslaanHandler(store, spelers, new InMemoryAvondStore(), NullLogger<VoorbereidingOpslaanHandler>.Instance)
            .HandleAsync(new VoorbereidingOpslaan { Datum = Wk48, Kaartjes = [annie.Id, kees.Id, bert.Id], Verwacht = 4 });

        store.Alle.Single().ZonderKaartje.Should().Equal(riet.Id);
        var formulier = await Scherm(store, annie, kees, riet, bert);
        formulier.Voorbereiding.Kaartjes.Should().BeEquivalentTo([annie.Id, kees.Id, bert.Id]);
    }

    [Test]
    public async Task someone_who_became_a_substitute_keeps_the_card_dad_gave_and_appears_among_the_substitutes()
    {
        var keesInvaller = kees with { Soort = Soort.Invaller };

        var formulier = await Scherm(new InMemoryVoorbereidingStore(Bewaard(annie, kees, riet)), annie, keesInvaller, riet);

        formulier.Invallers.Should().ContainSingle().Which.Id.Should().Be(kees.Id);
        formulier.Voorbereiding.Kaartjes.Should().BeEquivalentTo([annie.Id, kees.Id, riet.Id]);
    }

    [Test]
    public async Task a_substitutes_card_is_the_same_as_a_members()
    {
        var store = new InMemoryVoorbereidingStore(Bewaard(annie, kees, riet, bert) with { Blanco = 0 });
        var ophalen = new VoorbereidingOphalenHandler(store, new InMemorySpelerStore(annie, kees, riet, bert), new InMemoryAvondStore());

        var kaartjes = (await new KaartjesOphalenHandler(ophalen, new InMemoryInstellingenStore()).HandleAsync(new KaartjesOphalen { Datum = Wk48 })).Value;

        kaartjes.Lijst.Select(k => k.Naam).Should().Equal("Annie", "Bert", "Kees", "Riet");
        kaartjes.Lijst.Single(k => k.Naam == "Bert").Should().Be(new Kaartje { Naam = "Bert" });
    }

    [Test]
    public async Task a_substitute_with_a_card_is_already_on_the_entry_screen()
    {
        var store = new InMemoryVoorbereidingStore(Bewaard(annie, kees, riet, bert));
        var handler = new UitslagOphalenHandler(new InMemoryAvondStore(), new InMemorySpelerStore(annie, kees, riet, bert), new InMemoryInstellingenStore(), store);

        var formulier = (await handler.HandleAsync(new UitslagOphalen { Datum = Wk48 })).Value;

        formulier.Invallers.Should().ContainSingle().Which.Should().Match<UitslagRegel>(r => r.Speler.Id == bert.Id && r.Status == Status.Speelt);
        formulier.AndereInvallers.Should().BeEmpty();
    }

    [Test]
    public async Task a_substitute_is_never_a_slaper()
    {
        var store = new InMemoryVoorbereidingStore();
        var spelers = new InMemorySpelerStore(annie, kees, riet, bert, Lid("Ton Dekker"));
        await new VoorbereidingOpslaanHandler(store, spelers, new InMemoryAvondStore(), NullLogger<VoorbereidingOpslaanHandler>.Instance)
            .HandleAsync(new VoorbereidingOpslaan { Datum = Wk48, Kaartjes = [.. spelers.Alle.Select(s => s.Id)], Verwacht = 5, Slapers = [bert.Id], Voorstellen = true });

        // Five players: one sits out each round, so three slapers, all members.
        store.Alle.Single().Slapers.Should().HaveCount(3).And.NotContain(bert.Id).And.NotContain(Guid.Empty);
    }
}
