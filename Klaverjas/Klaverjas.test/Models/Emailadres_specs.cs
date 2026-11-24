namespace Klaverjas.test.Models;

public sealed class Emailadres_specs
{
    [TestCase("naam@voorbeeld.nl", true)]
    [TestCase(" naam@voorbeeld.nl ", true)]
    [TestCase("naam@voorbeeld", false)]
    [TestCase("naam voorbeeld.nl", false)]
    [TestCase("", false)]
    public void an_address_must_look_like_one(string adres, bool geldig)
    {
        Emailadres.IsGeldig(adres).Should().Be(geldig);
    }

    [Test]
    public void a_wrong_reply_address_or_an_empty_subject_is_refused()
    {
        Instellingen.Controleer(new Instellingen { Antwoordadres = "pa@" }).IsValid.Should().BeFalse();
        Instellingen.Controleer(new Instellingen { Onderwerp = " " }).IsValid.Should().BeFalse();
        Instellingen.Controleer(new Instellingen { Antwoordadres = "pa@voorbeeld.nl" }).IsValid.Should().BeTrue();
    }

    [Test]
    public void a_member_may_have_no_address_but_not_a_wrong_one()
    {
        var lid = new Speler { Naam = "Annie de Graaf", Soort = Soort.Lid };

        Speler.Controleer(lid).IsValid.Should().BeTrue();
        Speler.Controleer(lid with { Email = "annie@voorbeeld.nl" }).IsValid.Should().BeTrue();
        Speler.Controleer(lid with { Email = "annie at voorbeeld" }).IsValid.Should().BeFalse();
    }
}
