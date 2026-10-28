using Kluskat.application.Commands;
using Kluskat.application.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using static Kluskat.test.TestHelpers.Fixtures;

namespace Kluskat.test.Commands;

/// <summary>The game over the real handlers and in-memory stores.</summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class Spel_specs
{
    private readonly InMemoryEventStore events = new InMemoryEventStore();
    private readonly InMemoryKlusOverzichtStore overzichten = new InMemoryKlusOverzichtStore();
    private readonly InMemorySessieStore sessies = new InMemorySessieStore();
    private readonly InMemoryVoltooiingStore voltooiingen = new InMemoryVoltooiingStore();
    private readonly InMemoryProfielStore profielen = new InMemoryProfielStore();

    private IKlusRepository Repository => new InMemoryKlusRepository(events, overzichten);

    [Test]
    public async Task finishing_a_klus_skipped_earlier_today_is_brave()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        var klus = NieuweKlus(Data(), Nu.AddDays(-10));
        await Repository.OpslaanAsync(klus);

        await new KlusOverslaanHandler(Repository, sessies, NullLogger<KlusOverslaanHandler>.Instance)
            .HandleAsync(new KlusOverslaan { KlusId = klus.Id, Reden = OverslaanReden.GeenZin });
        await new KlusGedaanMeldenHandler(Repository, voltooiingen, sessies, NullLogger<KlusGedaanMeldenHandler>.Instance)
            .HandleAsync(new KlusGedaanMelden { KlusId = klus.Id });

        voltooiingen.Log.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Dapper = true, KeerOvergeslagen = 1, IntervalDagen = 7 });
    }

    [Test]
    public async Task a_locked_accessory_cannot_be_worn()
    {
        var result = await Kiezen(Accessoire.Kroon);

        result.IsValid.Should().BeFalse();
        profielen.Profiel.Accessoire.Should().Be(Accessoire.None);
    }

    [Test]
    public async Task an_unlocked_accessory_is_worn_and_shown()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        foreach (var voltooiing in Enumerable.Range(1, 6).Select(i => Voltooid(Nu.AddDays(-i))))
        {
            await voltooiingen.ToevoegenAsync(voltooiing);
        }

        (await Kiezen(Accessoire.Feestmuts)).IsValid.Should().BeTrue();

        var profiel = await new ProfielOphalenHandler(voltooiingen, overzichten, profielen).HandleAsync(new ProfielOphalen());
        profiel.Value.Accessoire.Should().Be(Accessoire.Feestmuts);
        profiel.Value.Spelstand.Level.Should().Be(2);
    }

    [Test]
    public async Task the_cat_takes_off_what_it_can_no_longer_wear()
    {
        await profielen.OpslaanAsync(new Profiel { Accessoire = Accessoire.Kroon });

        var profiel = await new ProfielOphalenHandler(voltooiingen, overzichten, profielen).HandleAsync(new ProfielOphalen());

        profiel.Value.Accessoire.Should().Be(Accessoire.None);
    }

    private Task<Result> Kiezen(Accessoire accessoire)
        => new AccessoireKiezenHandler(voltooiingen, overzichten, profielen, NullLogger<AccessoireKiezenHandler>.Instance)
            .HandleAsync(new AccessoireKiezen { Accessoire = accessoire });
}
