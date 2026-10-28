using Kluskat.application.Spel;
using static Kluskat.test.TestHelpers.Fixtures;

namespace Kluskat.test.Spel;

public sealed class Spelregels_specs
{
    private static readonly string[] GeenRuimtes = [];

    [TestCase(Grootte.Klein, false, false, 10)]
    [TestCase(Grootte.Middel, false, false, 25)]
    [TestCase(Grootte.Groot, false, false, 50)]
    [TestCase(Grootte.Middel, true, false, 37)]
    [TestCase(Grootte.Klein, true, true, 20)]
    public void xp_is_size_plus_bravery_plus_first_of_the_day(Grootte grootte, bool dapper, bool eerste, int xp)
    {
        Spelregels.Xp(Voltooid(Nu, grootte, dapper: dapper), eerste).Totaal.Should().Be(xp);
    }

    [TestCase(0, 1, 0)]
    [TestCase(59, 1, 59)]
    [TestCase(60, 2, 0)]
    [TestCase(139, 2, 79)]
    [TestCase(140, 3, 0)]
    public void levels_get_20_xp_longer_each(int xp, int level, int inLevel)
    {
        Spelregels.Niveau(xp).Should().Be((level, inLevel));
    }

    [TestCase(1, KatVorm.Kitten)]
    [TestCase(4, KatVorm.Kitten)]
    [TestCase(5, KatVorm.Kat)]
    [TestCase(14, KatVorm.Kat)]
    [TestCase(15, KatVorm.Majesteit)]
    public void the_cat_grows_with_its_level(int level, KatVorm vorm)
    {
        Spelregels.Vorm(level).Should().Be(vorm);
    }

    [Test]
    public void only_the_first_klus_of_a_local_day_gets_the_bonus()
    {
        var stand = Spelregels.Bereken([Voltooid(Nu), Voltooid(Nu.AddHours(1)), Voltooid(Nu.AddDays(1))], GeenRuimtes, Nu.AddDays(1));

        stand.Xp.Should().Be(10 + 5 + 10 + 10 + 5);
    }

    [Test]
    public void a_streak_counts_weeks_with_three_klussen()
    {
        var log = Week(new DateOnly(2026, 10, 05), 3)
            .Concat(Week(new DateOnly(2026, 10, 12), 3))
            .Concat(Week(new DateOnly(2026, 10, 19), 3))
            .Concat(Week(new DateOnly(2026, 10, 26), 1));

        var stand = Spelregels.Bereken(log, GeenRuimtes, Nu);

        stand.Reeks.Should().Be(3, because: "the running week does not break it while it can still count");
        stand.DezeWeek.Should().Be(1);
        stand.NogVoorDezeWeek.Should().Be(2);
    }

    [Test]
    public void a_week_short_without_a_freeze_starts_the_streak_over()
    {
        var log = Week(new DateOnly(2026, 10, 05), 3)
            .Concat(Week(new DateOnly(2026, 10, 12), 1))
            .Concat(Week(new DateOnly(2026, 10, 19), 3));

        var stand = Spelregels.Bereken(log, GeenRuimtes, Nu);

        stand.Reeks.Should().Be(1);
        stand.LangsteReeks.Should().Be(1);
    }

    [Test]
    public void four_weeks_earn_a_freeze_that_covers_a_short_week()
    {
        var log = Week(new DateOnly(2026, 09, 14), 3)
            .Concat(Week(new DateOnly(2026, 09, 21), 3))
            .Concat(Week(new DateOnly(2026, 09, 28), 3))
            .Concat(Week(new DateOnly(2026, 10, 05), 3))
            .Concat(Week(new DateOnly(2026, 10, 19), 3));

        var stand = Spelregels.Bereken(log, GeenRuimtes, Nu);

        stand.Reeks.Should().Be(5, because: "the empty week of 12 October was frozen, not counted");
        stand.Bevriezingen.Should().Be(0);
        stand.LangsteReeks.Should().Be(5);
    }

    [Test]
    public void the_running_week_adds_to_the_streak_once_it_counts()
    {
        var log = Week(new DateOnly(2026, 10, 19), 3).Concat(Week(new DateOnly(2026, 10, 26), 3));

        Spelregels.Bereken(log, GeenRuimtes, Nu).Reeks.Should().Be(2);
    }

    [Test]
    public void coming_back_after_two_weeks_is_rewarded()
    {
        var stand = Spelregels.Bereken([Voltooid(Nu.AddDays(-20)), Voltooid(Nu)], GeenRuimtes, Nu);

        stand.Trofeeen.Select(t => t.Id).Should().Contain("welkom-terug");
    }

    [Test]
    public void beating_a_klus_skipped_three_times_makes_an_overwinnaar()
    {
        var stand = Spelregels.Bereken([Voltooid(Nu, dapper: true, keerOvergeslagen: 3)], GeenRuimtes, Nu);

        stand.Trofeeen.Select(t => t.Id).Should().Contain(["dapper", "overwinnaar"]);
    }

    [Test]
    public void early_and_late_are_judged_on_amsterdam_time()
    {
        // 06:30 UTC is 07:30 in Amsterdam in winter: early. 22:30 UTC is 23:30: late.
        var vroeg = new DateTime(2026, 11, 03, 06, 30, 00, DateTimeKind.Utc);
        var laat = new DateTime(2026, 11, 03, 22, 30, 00, DateTimeKind.Utc);

        var stand = Spelregels.Bereken([Voltooid(vroeg), Voltooid(laat)], GeenRuimtes, laat);

        stand.Trofeeen.Select(t => t.Id).Should().Contain(["vroege-vogel", "nachtbraker"]);
    }

    [Test]
    public void rondje_huis_needs_every_room_of_the_list()
    {
        string[] ruimtes = ["Keuken", "Gang"];

        Spelregels.Bereken([Voltooid(Nu, ruimte: "Keuken")], ruimtes, Nu).Trofeeen.Should().NotContain(t => t.Id == "rondje-huis");
        Spelregels.Bereken([Voltooid(Nu, ruimte: "Keuken"), Voltooid(Nu, ruimte: "gang")], ruimtes, Nu).Trofeeen
            .Should().Contain(t => t.Id == "rondje-huis");
    }

    [Test]
    public void the_reward_shows_what_changed()
    {
        var eerder = Enumerable.Range(1, 6).Select(i => Voltooid(Nu.AddDays(-i))).ToList();
        var deze = Voltooid(Nu, Grootte.Groot);

        var beloning = Spelregels.Beloning([.. eerder, deze], deze, GeenRuimtes, Nu);

        beloning.Xp.Totaal.Should().Be(55);
        beloning.LevelOmhoog.Should().BeTrue(because: "6 × 15 = 90 XP is level 2; 55 more passes 140, level 3");
        beloning.NieuweAccessoires.Should().BeEmpty(because: "the party hat came at level 2, the bow waits for level 4");
        beloning.NieuweTrofeeen.Should().BeEmpty();
    }

    [Test]
    public void the_first_klus_ever_unlocks_a_trophy()
    {
        var deze = Voltooid(Nu);

        var beloning = Spelregels.Beloning([deze], deze, GeenRuimtes, Nu);

        beloning.NieuweTrofeeen.Select(t => t.Id).Should().Equal("eerste");
    }

    [Test]
    public void accessories_unlock_with_levels()
    {
        new Spelstand { Level = 1 }.Accessoires.Should().BeEmpty();
        new Spelstand { Level = 4 }.Accessoires.Should().Equal(Accessoire.Feestmuts, Accessoire.Strikje);
        new Spelstand { Level = 15 }.Accessoires.Should().HaveCount(7);
    }

    [Test]
    public void every_trophy_has_a_unique_id()
    {
        Trofeeen.Alle.Select(t => t.Id).Should().OnlyHaveUniqueItems();
    }
}
