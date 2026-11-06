using System.Net;
using System.Net.Http;

namespace CharacterCreator.test.Web;

/// <summary>The page shell: the crest bar, the theme from the shared cookie, and the anonymous routes.</summary>
public sealed class Shell_specs
{
    private CharacterCreatorWebApplicationFactory factory = null!;

    [OneTimeSetUp]
    public void Boot() => factory = new CharacterCreatorWebApplicationFactory();

    [OneTimeTearDown]
    public void Shutdown() => factory.Dispose();

    [Test]
    public async Task the_build_screen_is_the_home_page_and_wears_the_crest_bar()
    {
        var html = await GetHtml("/");

        html.Should().Contain("class=\"vtx-header\"")
            .And.Contain("Vortex Adventures")
            .And.Contain("Character Creator")
            .And.Contain("Personage bouwen");
    }

    [Test]
    public async Task the_build_screen_is_prerendered_on_the_server_and_marked_for_the_browser()
    {
        var html = await GetHtml("/");

        html.Should().Contain("data-render-state=\"server\"", "the server prerenders the page first")
            .And.Contain("Blazor:", "the page carries the marker the browser takes it over from");
    }

    [Test]
    public async Task the_page_loads_guild_hall_and_the_theme_script()
    {
        var html = await GetHtml("/");

        html.Should().Contain("lib/vortex-tools/vortex-theme")
            .And.Contain("lib/vortex-tools/guildhall");
    }

    [TestCase("dark", "data-bs-theme=\"dark\"")]
    [TestCase("light", "data-bs-theme=\"light\"")]
    public async Task the_theme_comes_from_the_shared_cookie(string cookie, string attribute)
    {
        var html = await GetHtml("/", cookie);

        html.Should().Contain(attribute);
    }

    [TestCase(null)]
    [TestCase("\"><script>alert(1)</script>")]
    public async Task without_a_known_theme_the_attribute_is_left_off_and_the_cookie_never_echoed(string? cookie)
    {
        var html = await GetHtml("/", cookie);

        html.Should().NotContain("data-bs-theme", "the script then follows the system setting")
            .And.NotContain("alert(1)");
    }

    [TestCase("/voorbeelden", "Voorbeelden")]
    [TestCase("/vragen", "Vragen")]
    [TestCase("/aanmelden-niet-ingesteld", "Aanmelden is hier niet mogelijk")]
    public async Task the_other_pages_are_open_to_anyone(string path, string heading)
    {
        var html = await GetHtml(path);

        html.Should().Contain(heading);
    }

    [Test]
    public async Task an_anonymous_visitor_is_offered_a_sign_in()
    {
        var html = await GetHtml("/");

        html.Should().Contain(">Aanmelden<").And.NotContain("Afmelden");
    }

    [Test]
    public async Task without_an_eru_sign_in_aanmelden_explains_why()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri("/aanmelden", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/aanmelden-niet-ingesteld");
    }

    [Test]
    public async Task the_health_probe_answers_without_touching_anything()
    {
        using var client = factory.CreateAnonymousClient();

        var body = await client.GetStringAsync(new Uri("/healthz", UriKind.Relative));

        body.Should().Be("ok");
    }

    [Test]
    public async Task an_unknown_page_says_so()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(new Uri("/bestaat-niet", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Niet gevonden");
    }

    private async Task<string> GetHtml(string path, string? themeCookie = null)
    {
        using var client = factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (themeCookie is not null)
        {
            request.Headers.Add("Cookie", $"vortex-theme={Uri.EscapeDataString(themeCookie)}");
        }

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} should render");
        return await response.Content.ReadAsStringAsync();
    }
}
