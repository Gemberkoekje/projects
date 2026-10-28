using static Kluskat.test.TestHelpers.Fixtures;

namespace Kluskat.test.Kiezen;

public sealed class KlusKiezer_specs
{
    [Test]
    public void picks_the_klus_most_behind_relative_to_its_interval()
    {
        // A weekly klus three days late is further behind than a yearly klus thirty days late.
        var wekelijks = Overzicht("Stofzuigen", dagen: 7, dagenGeleden: 10);
        var jaarlijks = Overzicht("Gordijnen", dagen: 365, dagenGeleden: 395);

        var keuze = KlusKiezer.Kies([jaarlijks, wekelijks], new Sessie(), Nu, vooruit: false);

        keuze.Uitkomst.Should().Be(KeuzeUitkomst.Klus);
        keuze.Klus.Should().Be(wekelijks, because: "10/7 intervals behind beats 395/365");
    }

    [Test]
    public void never_offers_a_klus_that_is_not_due()
    {
        var keuze = KlusKiezer.Kies([Overzicht("Stofzuigen", dagen: 7, dagenGeleden: 3)], new Sessie(), Nu, vooruit: false);

        keuze.Uitkomst.Should().Be(KeuzeUitkomst.NiksTeDoen);
    }

    [Test]
    public void hides_a_skipped_klus_and_offers_the_next_one()
    {
        var eerste = Overzicht("Stofzuigen", dagen: 7, dagenGeleden: 14);
        var tweede = Overzicht("Dweilen", dagen: 7, dagenGeleden: 8);
        var sessie = new Sessie().NaOverslaan(eerste, OverslaanReden.GeenZin, Nu);

        var keuze = KlusKiezer.Kies([eerste, tweede], sessie, Nu, vooruit: false);

        keuze.Klus.Should().Be(tweede);
    }

    [Test]
    public void brings_a_skipped_klus_back_first_once_the_window_wears_off()
    {
        var eerste = Overzicht("Stofzuigen", dagen: 7, dagenGeleden: 14);
        var tweede = Overzicht("Dweilen", dagen: 7, dagenGeleden: 8);
        var later = Nu.Add(Sessie.Levensduur).AddMinutes(1);
        var sessie = new Sessie().NaOverslaan(eerste, OverslaanReden.GeenZin, Nu).Actueel(later);

        var keuze = KlusKiezer.Kies([eerste, tweede], sessie, later, vooruit: false);

        keuze.Klus.Should().Be(eerste);
    }

    [Test]
    public void says_everything_was_skipped_rather_than_nothing_to_do()
    {
        var enige = Overzicht("Stofzuigen", dagen: 7, dagenGeleden: 14);
        var sessie = new Sessie().NaOverslaan(enige, OverslaanReden.GeenZin, Nu);

        var keuze = KlusKiezer.Kies([enige], sessie, Nu, vooruit: false);

        keuze.Uitkomst.Should().Be(KeuzeUitkomst.AllesOvergeslagen);
    }

    [Test]
    public void geen_tijd_on_a_middel_klus_only_offers_klein_ones_after()
    {
        var middel = Overzicht("Oven", dagen: 14, dagenGeleden: 40, Grootte.Middel);
        var groot = Overzicht("Ramen", dagen: 84, dagenGeleden: 400, Grootte.Groot);
        var klein = Overzicht("Deurknop", dagen: 14, dagenGeleden: 15, Grootte.Klein);
        var sessie = new Sessie().NaOverslaan(middel, OverslaanReden.GeenTijd, Nu);

        var keuze = KlusKiezer.Kies([middel, groot, klein], sessie, Nu, vooruit: false);

        keuze.Klus.Should().Be(klein);
        keuze.KleinGehouden.Should().BeTrue();
    }

    [Test]
    public void ignores_geschrapte_klussen()
    {
        var geschrapt = Overzicht("Weg", dagen: 7, dagenGeleden: 100) with { Geschrapt = true };

        var keuze = KlusKiezer.Kies([geschrapt], new Sessie(), Nu, vooruit: false);

        keuze.Uitkomst.Should().Be(KeuzeUitkomst.NiksTeDoen);
    }

    [Test]
    public void getting_ahead_offers_the_klus_closest_to_due()
    {
        var bijna = Overzicht("Stofzuigen", dagen: 7, dagenGeleden: 6);
        var vers = Overzicht("Dweilen", dagen: 14, dagenGeleden: 1);

        var keuze = KlusKiezer.Kies([vers, bijna], new Sessie(), Nu, vooruit: true);

        keuze.Klus.Should().Be(bijna);
        keuze.Vooruit.Should().BeTrue();
    }

    [Test]
    public void breaks_ties_with_the_smaller_klus()
    {
        var groot = Overzicht("Ramen", dagen: 7, dagenGeleden: 14, Grootte.Groot);
        var klein = Overzicht("Deurknop", dagen: 7, dagenGeleden: 14, Grootte.Klein);

        var keuze = KlusKiezer.Kies([groot, klein], new Sessie(), Nu, vooruit: false);

        keuze.Klus.Should().Be(klein);
    }
}
