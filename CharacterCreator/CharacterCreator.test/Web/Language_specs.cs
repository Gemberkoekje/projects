using System.Net;
using System.Net.Http;

namespace CharacterCreator.test.Web;

/// <summary>NL and EN (DESIGN.md decision 11): the language cookie, the switch, and what it changes.</summary>
public sealed class Language_specs
{
    private CharacterCreatorWebApplicationFactory factory = null!;

    [OneTimeSetUp]
    public void Boot() => factory = new CharacterCreatorWebApplicationFactory();

    [OneTimeTearDown]
    public void Shutdown() => factory.Dispose();

    [Test]
    public async Task without_a_choice_the_page_is_dutch()
    {
        var html = await GetHtml("/");

        html.Should().Contain("<html lang=\"nl\"")
            .And.Contain("Personage bouwen")
            .And.Contain(">Voorbeelden<")
            .And.Contain("href=\"taal/en?returnUrl=%2F\"");
    }

    [Test]
    public async Task with_english_chosen_the_page_and_the_prerendered_build_screen_are_english()
    {
        var html = await GetHtml("/", "en");

        html.Should().Contain("<html lang=\"en\"")
            .And.Contain("Build a character")
            .And.Contain("Loading the build screen")
            .And.Contain(">Presets<")
            .And.Contain("href=\"taal/nl?returnUrl=%2F\"");
    }

    [Test]
    public async Task switching_sets_the_cookie_for_a_year_and_goes_back()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri("/taal/en?returnUrl=%2Fvoorbeelden", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/voorbeelden");
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.Should().StartWith("charactercreator.taal=en;")
            .And.Contain("max-age=31536000")
            .And.Contain("secure")
            .And.Contain("samesite=lax")
            .And.Contain("httponly");
    }

    [TestCase("https://evil.example/")]
    [TestCase("//evil.example/")]
    [TestCase("")]
    public async Task switching_never_leaves_the_app(string returnUrl)
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri($"/taal/en?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative));

        response.Headers.Location!.ToString().Should().Be("/");
    }

    [Test]
    public async Task an_unknown_language_is_dutch()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri("/taal/fr", UriKind.Relative));

        response.Headers.GetValues("Set-Cookie").Single().Should().StartWith("charactercreator.taal=nl;");
    }

    [Test]
    public async Task the_faq_speaks_both_languages()
    {
        (await GetHtml("/vragen")).Should().Contain("Kan ik mijn personage uit de oude builder meenemen?");
        (await GetHtml("/vragen", "en")).Should().Contain("Can I bring my character from the old builder?");
    }

    private async Task<string> GetHtml(string path, string language = "")
    {
        using var client = factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (language.Length > 0)
        {
            request.Headers.Add("Cookie", $"charactercreator.taal={language}");
        }

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} should render");
        return await response.Content.ReadAsStringAsync();
    }
}
