namespace Klaverjas.test.Models;

public sealed class Wachtwoord_specs
{
    [Test]
    public void a_hashed_password_matches_itself_and_nothing_else()
    {
        var hash = Wachtwoord.Maak("Klaver-7");

        Wachtwoord.Klopt("Klaver-7", hash).Should().BeTrue();
        Wachtwoord.Klopt("klaver-7", hash).Should().BeFalse();
        Wachtwoord.Klopt(string.Empty, hash).Should().BeFalse();
    }

    [Test]
    public void the_same_password_hashes_differently_each_time()
    {
        Wachtwoord.Maak("Klaver-7").Should().NotBe(Wachtwoord.Maak("Klaver-7"));
    }

    [Test]
    public void the_hash_does_not_contain_the_password()
    {
        Wachtwoord.Maak("Klaver-7").Should().NotContain("Klaver-7");
    }

    [TestCase("")]
    [TestCase("Klaver-7")]
    [TestCase("pbkdf2-sha256$abc$AAAA$AAAA")]
    [TestCase("pbkdf2-sha256$1000$niet-base64$AAAA")]
    public void a_stored_value_that_is_no_hash_never_matches(string opgeslagen)
    {
        Wachtwoord.Klopt("Klaver-7", opgeslagen).Should().BeFalse();
    }

    [Test]
    public void a_players_password_shorter_than_four_characters_is_refused()
    {
        Instellingen.Controleer(new Instellingen { Spelerswachtwoord = "abc" }).IsValid.Should().BeFalse();
        Instellingen.Controleer(new Instellingen { Spelerswachtwoord = "abcd" }).IsValid.Should().BeTrue();
        Instellingen.Controleer(new Instellingen()).IsValid.Should().BeTrue();
    }
}
