using Klaverjas.application.Commands;
using Klaverjas.application.Queries;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Commands;

public sealed class StandOnline_specs
{
    private readonly Speler annie = Lid("Annie de Graaf");
    private readonly Speler kees = Lid("Kees Bakker");
    private InMemoryAvondStore avonden = new InMemoryAvondStore();
    private InMemoryInstellingenStore instellingen = new InMemoryInstellingenStore();
    private InMemoryOnlineStandStore online = new InMemoryOnlineStandStore();
    private InMemorySpelerStore spelers = new InMemorySpelerStore();

    // NUnit shares one instance across the tests: every test starts from empty stores.
    [SetUp]
    public void LegeStores()
    {
        avonden = new InMemoryAvondStore();
        instellingen = new InMemoryInstellingenStore();
        online = new InMemoryOnlineStandStore();
        spelers = new InMemorySpelerStore(annie, kees);
    }

    private StandOnlineZettenHandler OnlineZetten()
        => new StandOnlineZettenHandler(avonden, spelers, instellingen, online, NullLogger<StandOnlineZettenHandler>.Instance);

    private async Task<StandOverzicht> StandAsync()
        => (await new StandOphalenHandler(avonden, spelers, instellingen, online).HandleAsync(new StandOphalen())).Value;

    [Test]
    public async Task nothing_goes_online_before_the_first_evening()
    {
        var result = await OnlineZetten().HandleAsync(new StandOnlineZetten());

        result.IsValid.Should().BeFalse();
        online.Huidig.IsLeeg.Should().BeTrue();
    }

    [Test]
    public async Task the_standings_go_online_with_the_note_and_the_time()
    {
        await avonden.OpslaanAsync(Avond(Wk47, Speelt(annie, 4800), Speelt(kees, 4500)));

        using (Clock.SetTimeForCurrentContext(() => new DateTime(2026, 11, 16, 21, 30, 00, DateTimeKind.Utc)))
        {
            (await OnlineZetten().HandleAsync(new StandOnlineZetten { Bericht = "  Tot maandag!  " })).IsValid.Should().BeTrue();
        }

        online.Huidig.Should().Match<OnlineStand>(o => o.Bericht == "Tot maandag!" && o.OnlineSinds == new DateTime(2026, 11, 16, 21, 30, 00, DateTimeKind.Utc));
        online.Huidig.Stand.LaatsteAvond.Should().Be(Wk47);
        online.Huidig.Stand.Winnaars.First().Naam.Should().Be("Annie de Graaf");
    }

    [Test]
    public async Task a_note_that_is_too_long_is_refused()
    {
        await avonden.OpslaanAsync(Avond(Wk47, Speelt(annie, 4800)));

        var result = await OnlineZetten().HandleAsync(new StandOnlineZetten { Bericht = new string('x', OnlineStand.MaxBericht + 1) });

        result.IsValid.Should().BeFalse();
        online.Huidig.IsLeeg.Should().BeTrue();
    }

    [Test]
    public async Task a_correction_after_going_online_asks_for_the_improved_standings()
    {
        await avonden.OpslaanAsync(Avond(Wk47, Speelt(annie, 4800), Speelt(kees, 4500)));
        (await StandAsync()).StaatOnline.Should().BeFalse();

        await OnlineZetten().HandleAsync(new StandOnlineZetten());
        (await StandAsync()).Should().Match<StandOverzicht>(s => s.StaatOnline && !s.IsGewijzigd);

        await avonden.OpslaanAsync(Avond(Wk47, Speelt(annie, 4800), Speelt(kees, 4900)));
        (await StandAsync()).Should().Match<StandOverzicht>(s => !s.StaatOnline && s.IsGewijzigd);

        await OnlineZetten().HandleAsync(new StandOnlineZetten());
        (await StandAsync()).IsGewijzigd.Should().BeFalse();
    }

    [Test]
    public async Task the_players_see_what_went_online_not_the_correction()
    {
        await avonden.OpslaanAsync(Avond(Wk47, Speelt(annie, 4800), Speelt(kees, 4500)));
        await OnlineZetten().HandleAsync(new StandOnlineZetten());
        await avonden.OpslaanAsync(Avond(Wk47, Speelt(annie, 4800), Speelt(kees, 4900)));

        var getoond = (await new OnlineStandOphalenHandler(online).HandleAsync(new OnlineStandOphalen())).Value;

        getoond.Stand.Winnaars.First().Naam.Should().Be("Annie de Graaf");
    }

    [Test]
    public async Task new_colours_do_not_count_as_a_change_but_a_new_name_does()
    {
        await avonden.OpslaanAsync(Avond(Wk47, Speelt(annie, 4800), Speelt(kees, 4500)));
        await OnlineZetten().HandleAsync(new StandOnlineZetten());

        await instellingen.OpslaanAsync(Standaard with { Kleuren = new Kleuren { Eerste = "#ff0000" } });
        (await StandAsync()).IsGewijzigd.Should().BeFalse();

        await spelers.OpslaanAsync(kees with { Naam = "Kees Bakker-de Vries" });
        (await StandAsync()).IsGewijzigd.Should().BeTrue();
    }

    [Test]
    public async Task the_evenings_list_shows_what_is_online()
    {
        await avonden.OpslaanAsync(Avond(Wk46, Speelt(annie, 4000), Speelt(kees, 4100)));
        await OnlineZetten().HandleAsync(new StandOnlineZetten());
        await avonden.OpslaanAsync(Avond(Wk47, Speelt(annie, 4800), Speelt(kees, 4500)));
        var handler = new AvondenOphalenHandler(avonden, instellingen, new InMemoryVoorbereidingStore(), spelers, online, new InMemoryVerzendingStore());

        var overzicht = (await handler.HandleAsync(new AvondenOphalen())).Value;

        overzicht.OnlineTot.Should().Be(Wk46);
        overzicht.IsOnline(Wk46).Should().BeTrue();
        overzicht.IsOnline(Wk47).Should().BeFalse();
        overzicht.OnlineGewijzigd.Should().BeTrue();
    }
}
