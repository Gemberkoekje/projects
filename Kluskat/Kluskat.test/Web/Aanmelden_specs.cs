using Kluskat.WebApp.Pages;

namespace Kluskat.test.Web;

public sealed class Aanmelden_specs
{
    [TestCase("geheim", "geheim", true)]
    [TestCase("geheim", "Geheim", false)]
    [TestCase("", "geheim", false)]
    [TestCase("geheim!", "geheim", false)]
    public void the_password_must_match_exactly(string gegeven, string verwacht, bool klopt)
    {
        AanmeldenModel.Klopt(gegeven, verwacht).Should().Be(klopt);
    }
}
