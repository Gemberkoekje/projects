using Klaverjas.WebApp.Pages.Beheer;

namespace Klaverjas.test.Web;

/// <summary>The phone number in Dad's player list, so he can call a substitute (2026-11-26).</summary>
public sealed class Spelerslijst_specs
{
    [TestCase("06-12345678", "0612345678")]
    [TestCase("06 1234 5678", "0612345678")]
    [TestCase(" 0612345678 ", "0612345678")]
    [TestCase("+31 6 12345678", "+31612345678")]
    [TestCase("(0512) 54 32 10", "0512543210")]
    public void a_phone_number_becomes_a_link_to_call(string telefoon, string link)
    {
        SpelersModel.BelLink(telefoon).Should().Be(link);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("via zijn dochter")]
    [TestCase("06-12345678 (na 18 uur)")]
    public void without_a_plain_number_there_is_no_link(string telefoon)
    {
        SpelersModel.BelLink(telefoon).Should().BeEmpty();
    }
}
