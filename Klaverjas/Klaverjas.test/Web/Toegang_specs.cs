using Klaverjas.WebApp;

namespace Klaverjas.test.Web;

public sealed class Toegang_specs
{
    private const string Config = "uit-de-configuratie";

    [TestCase("geheim", "geheim", true)]
    [TestCase("geheim", "Geheim", false)]
    [TestCase("", "geheim", false)]
    [TestCase("geheim!", "geheim", false)]
    public void a_compared_password_must_match_exactly(string gegeven, string verwacht, bool klopt)
    {
        Toegang.Klopt(gegeven, verwacht).Should().Be(klopt);
    }

    [TestCase("klaverjas", true)]
    [TestCase("Klaverjas", true)]
    [TestCase(" KLAVERJAS ", true)]
    [TestCase("klaverjassen", false)]
    [TestCase("", false)]
    public void the_players_password_ignores_capitals_and_spaces_at_the_ends(string gegeven, bool klopt)
    {
        var instellingen = new Instellingen { Spelerswachtwoord = "Klaverjas" };

        Toegang.KloptSpelers(gegeven, instellingen).Should().Be(klopt);
    }

    [Test]
    public void nothing_opens_the_players_page_while_it_has_no_password()
    {
        Toegang.KloptSpelers(string.Empty, new Instellingen()).Should().BeFalse();
    }

    [Test]
    public void dads_own_password_and_the_configured_one_both_open_the_admin()
    {
        var instellingen = new Instellingen { BeheerWachtwoord = Wachtwoord.Maak("zelfgekozen") };

        Toegang.KloptBeheer("zelfgekozen", instellingen, Config).Should().BeTrue();
        Toegang.KloptBeheer(Config, instellingen, Config).Should().BeTrue();
        Toegang.KloptBeheer("Zelfgekozen", instellingen, Config).Should().BeFalse();
    }

    [Test]
    public void changing_the_players_password_changes_their_key_but_not_dads()
    {
        var oud = new Instellingen { Spelerswachtwoord = "klaverjas" };
        var nieuw = oud with { Spelerswachtwoord = "harten" };

        Toegang.Sleutel(Toegang.Speler, nieuw, Config).Should().NotBe(Toegang.Sleutel(Toegang.Speler, oud, Config));
        Toegang.Sleutel(Toegang.Beheer, nieuw, Config).Should().Be(Toegang.Sleutel(Toegang.Beheer, oud, Config));
    }

    [Test]
    public void retyping_the_players_password_with_capitals_keeps_everyone_signed_in()
    {
        var oud = new Instellingen { Spelerswachtwoord = "klaverjas" };

        Toegang.Sleutel(Toegang.Speler, oud with { Spelerswachtwoord = "Klaverjas" }, Config).Should().Be(Toegang.Sleutel(Toegang.Speler, oud, Config));
    }

    [Test]
    public void choosing_his_own_password_changes_dads_key()
    {
        var oud = new Instellingen();
        var nieuw = oud with { BeheerWachtwoord = Wachtwoord.Maak("zelfgekozen") };

        Toegang.Sleutel(Toegang.Beheer, nieuw, Config).Should().NotBe(Toegang.Sleutel(Toegang.Beheer, oud, Config));
    }

    [Test]
    public void an_unknown_role_has_no_key()
    {
        Toegang.Sleutel("bezoeker", new Instellingen(), Config).Should().BeEmpty();
    }
}
