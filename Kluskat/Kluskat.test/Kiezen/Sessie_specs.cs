using static Kluskat.test.TestHelpers.Fixtures;

namespace Kluskat.test.Kiezen;

public sealed class Sessie_specs
{
    [Test]
    public void wears_off_three_hours_after_the_last_tap()
    {
        var klus = Overzicht("Stofzuigen", dagen: 7, dagenGeleden: 10);
        var sessie = new Sessie().NaOverslaan(klus, OverslaanReden.GeenZin, Nu);

        sessie.Actueel(Nu.AddHours(2.9)).IsOvergeslagen(klus.Id).Should().BeTrue();
        sessie.Actueel(Nu.AddHours(3.1)).IsOvergeslagen(klus.Id).Should().BeFalse();
    }

    [Test]
    public void every_tap_extends_the_window()
    {
        var eerste = Overzicht("Stofzuigen", dagen: 7, dagenGeleden: 10);
        var tweede = Overzicht("Dweilen", dagen: 7, dagenGeleden: 10);

        var sessie = new Sessie()
            .NaOverslaan(eerste, OverslaanReden.GeenZin, Nu)
            .NaOverslaan(tweede, OverslaanReden.GeenZin, Nu.AddHours(2));

        sessie.Actueel(Nu.AddHours(4)).IsOvergeslagen(eerste.Id).Should().BeTrue();
    }

    [TestCase(Grootte.Groot, Grootte.Middel)]
    [TestCase(Grootte.Middel, Grootte.Klein)]
    [TestCase(Grootte.Klein, Grootte.Klein)]
    public void geen_tijd_caps_below_the_skipped_size(Grootte overgeslagen, Grootte max)
    {
        var klus = Overzicht("Iets", dagen: 7, dagenGeleden: 10, overgeslagen);

        new Sessie().NaOverslaan(klus, OverslaanReden.GeenTijd, Nu).MaxGrootte.Should().Be(max);
    }

    [Test]
    public void the_cap_only_tightens()
    {
        var klein = Overzicht("Deurknop", dagen: 7, dagenGeleden: 10, Grootte.Klein);
        var groot = Overzicht("Ramen", dagen: 7, dagenGeleden: 10, Grootte.Groot);

        var sessie = new Sessie()
            .NaOverslaan(klein, OverslaanReden.GeenTijd, Nu)
            .NaOverslaan(groot, OverslaanReden.GeenTijd, Nu);

        sessie.MaxGrootte.Should().Be(Grootte.Klein);
    }

    [Test]
    public void geen_zin_sets_no_cap()
    {
        var klus = Overzicht("Ramen", dagen: 7, dagenGeleden: 10, Grootte.Groot);

        new Sessie().NaOverslaan(klus, OverslaanReden.GeenZin, Nu).MaxGrootte.Should().Be(Grootte.None);
    }

    [Test]
    public void gedaan_removes_the_klus_from_the_skips_but_keeps_the_cap()
    {
        var klus = Overzicht("Oven", dagen: 7, dagenGeleden: 10, Grootte.Middel);

        var sessie = new Sessie()
            .NaOverslaan(klus, OverslaanReden.GeenTijd, Nu)
            .NaGedaan(klus.Id, Nu.AddMinutes(5));

        sessie.IsOvergeslagen(klus.Id).Should().BeFalse();
        sessie.MaxGrootte.Should().Be(Grootte.Klein);
    }
}
