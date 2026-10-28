using Kluskat.application.Commands;
using Kluskat.application.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using static Kluskat.test.TestHelpers.Fixtures;

namespace Kluskat.test.Commands;

/// <summary>The klus screen end to end, over the real handlers and in-memory stores.</summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class Klusscherm_specs
{
    private readonly InMemoryEventStore events = new InMemoryEventStore();
    private readonly InMemoryKlusOverzichtStore overzichten = new InMemoryKlusOverzichtStore();
    private readonly InMemorySessieStore sessies = new InMemorySessieStore();
    private readonly InMemoryVoltooiingStore voltooiingen = new InMemoryVoltooiingStore();

    private IKlusRepository Repository => new InMemoryKlusRepository(events, overzichten);

    [Test]
    public async Task gedaan_logs_a_completion_that_the_win_screen_counts()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        var id = await ToevoegenAsync(Data(), Nu.AddDays(-10));

        var result = await new KlusGedaanMeldenHandler(Repository, voltooiingen, sessies, NullLogger<KlusGedaanMeldenHandler>.Instance)
            .HandleAsync(new KlusGedaanMelden { KlusId = id });

        result.IsValid.Should().BeTrue();
        var winst = await new OverwinningOphalenHandler(overzichten, voltooiingen).HandleAsync(new OverwinningOphalen { KlusId = id });
        winst.Value.AantalDezeWeek.Should().Be(1);
        winst.Value.Klus.KanTerugdraaien.Should().BeTrue();
    }

    [Test]
    public async Task a_done_klus_is_no_longer_offered()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        var id = await ToevoegenAsync(Data(), Nu.AddDays(-10));

        await new KlusGedaanMeldenHandler(Repository, voltooiingen, sessies, NullLogger<KlusGedaanMeldenHandler>.Instance)
            .HandleAsync(new KlusGedaanMelden { KlusId = id });

        var keuze = await VolgendeAsync();
        keuze.Uitkomst.Should().Be(KeuzeUitkomst.NiksTeDoen);
    }

    [Test]
    public async Task terugdraaien_brings_the_klus_back_and_uncounts_it()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        var id = await ToevoegenAsync(Data(), Nu.AddDays(-10));
        await new KlusGedaanMeldenHandler(Repository, voltooiingen, sessies, NullLogger<KlusGedaanMeldenHandler>.Instance)
            .HandleAsync(new KlusGedaanMelden { KlusId = id });

        var result = await new GedaanTerugdraaienHandler(Repository, voltooiingen, NullLogger<GedaanTerugdraaienHandler>.Instance)
            .HandleAsync(new GedaanTerugdraaien { KlusId = id });

        result.IsValid.Should().BeTrue();
        voltooiingen.Log.Should().BeEmpty();
        (await VolgendeAsync()).Klus.KlusId.Should().Be(id);
    }

    [Test]
    public async Task geen_tijd_records_the_skip_on_the_stream_and_caps_the_size()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        var groot = await ToevoegenAsync(Data("Ramen", 84, Grootte.Groot), Nu.AddDays(-400));
        var klein = await ToevoegenAsync(Data("Deurknop", 14, Grootte.Klein), Nu.AddDays(-15));

        var result = await new KlusOverslaanHandler(Repository, sessies, NullLogger<KlusOverslaanHandler>.Instance)
            .HandleAsync(new KlusOverslaan { KlusId = groot, Reden = OverslaanReden.GeenTijd });

        result.IsValid.Should().BeTrue();
        events.Stream(groot).OfType<KlusOvergeslagen>().Should().ContainSingle();
        sessies.Sessie.MaxGrootte.Should().Be(Grootte.Middel);
        (await VolgendeAsync()).Klus.KlusId.Should().Be(klein);
    }

    [Test]
    public async Task startvulling_seeds_once()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        var handler = new StartvullingToepassenHandler(Repository, overzichten, NullLogger<StartvullingToepassenHandler>.Instance);
        var command = new StartvullingToepassen { Klussen = [Data("Een"), Data("Twee")] };

        (await handler.HandleAsync(command)).IsValid.Should().BeTrue();
        (await handler.HandleAsync(command)).IsValid.Should().BeTrue();

        overzichten.Alle.Should().HaveCount(2);
    }

    private async Task<KlusId> ToevoegenAsync(Klus.Data data, DateTime laatstGedaan)
    {
        var klus = NieuweKlus(data, laatstGedaan);
        (await Repository.OpslaanAsync(klus)).IsValid.Should().BeTrue();
        return klus.Id;
    }

    private async Task<Keuze> VolgendeAsync()
        => (await new VolgendeKlusOphalenHandler(overzichten, sessies).HandleAsync(new VolgendeKlusOphalen())).Value;
}
