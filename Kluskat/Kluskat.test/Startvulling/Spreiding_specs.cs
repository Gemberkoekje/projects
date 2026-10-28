using Kluskat.application.Startvulling;
using Kluskat.infrastructure.Services;
using static Kluskat.test.TestHelpers.Fixtures;

namespace Kluskat.test.Startvulling;

public sealed class Spreiding_specs
{
    [Test]
    public void the_first_klus_of_every_interval_is_due_today()
    {
        int[] intervallen = [7, 7, 7, 14, 365];

        var laatstGedaan = Spreiding.LaatstGedaan(intervallen, Nu);

        Kalender.Datum(laatstGedaan[0].AddDays(7)).Should().Be(Kalender.Datum(Nu));
        Kalender.Datum(laatstGedaan[3].AddDays(14)).Should().Be(Kalender.Datum(Nu));
        Kalender.Datum(laatstGedaan[4].AddDays(365)).Should().Be(Kalender.Datum(Nu));
    }

    [Test]
    public void spreads_a_group_evenly_over_its_interval()
    {
        int[] intervallen = [.. Enumerable.Repeat(7, 7)];

        var eersteKeren = Spreiding.LaatstGedaan(intervallen, Nu)
            .Select(d => Kalender.Datum(d.AddDays(7)).DayNumber - Kalender.Datum(Nu).DayNumber);

        eersteKeren.Should().Equal(0, 1, 2, 3, 4, 5, 6);
    }

    [Test]
    public void becomes_due_at_local_midnight()
    {
        var laatstGedaan = Spreiding.LaatstGedaan([7], Nu)[0];

        laatstGedaan.AddDays(7).Should().Be(Kalender.Middernacht(Kalender.Datum(Nu)));
    }

    [Test]
    public void the_seed_list_is_valid_and_staggered_to_a_trickle()
    {
        var klussen = StartvullingService.Lezen();
        var laatstGedaan = Spreiding.LaatstGedaan([.. klussen.Select(k => k.IntervalDagen)], Nu);

        klussen.Should().HaveCountGreaterThan(50);
        klussen.Select((data, i) => Klus.Toevoegen(data, laatstGedaan[i]).IsValid).Should().AllBeEquivalentTo(true);

        // Day one: one klus per interval group plus a second weekly one — a handful, not the whole sheet.
        var vandaag = klussen.Where((data, i) => laatstGedaan[i].AddDays(data.IntervalDagen) <= Nu).Count();
        vandaag.Should().BeLessThan(12);
    }
}
