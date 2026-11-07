using System.Net;
using System.Net.Http;
using CharacterCreator.WebApp.Authorization;

namespace CharacterCreator.test.Web;

/// <summary><c>Beheer → Rulesets</c>: SL and EC only (DESIGN.md decision 15), and what it shows them.</summary>
public sealed class Beheer_rulesets_specs
{
    private static readonly Uri Rulesets = new ("/beheer/rulesets", UriKind.Relative);

    private CharacterCreatorWebApplicationFactory factory = null!;

    [OneTimeSetUp]
    public void Boot() => factory = new CharacterCreatorWebApplicationFactory();

    [OneTimeTearDown]
    public void Shutdown() => factory.Dispose();

    [Test]
    public async Task an_anonymous_visitor_is_sent_to_sign_in_and_back()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(Rulesets);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.AbsolutePath.Should().Be("/aanmelden");
        location.Query.Should().Be("?returnUrl=%2Fbeheer%2Frulesets");
    }

    [TestCase]
    [TestCase(EruPermissions.VoidInfobalie)]
    public async Task a_player_without_the_role_is_told_there_is_no_access(params string[] permissions)
    {
        using var client = factory.CreateSignedInClient("Pim", permissions);

        using var response = await client.GetAsync(Rulesets);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/geen-toegang");
        (await GetHtml(client, response.Headers.Location)).Should().Contain("Geen toegang").And.Contain("SL en EC");
    }

    [TestCase(EruPermissions.VoidReferee)]
    [TestCase(EruPermissions.VoidEventControl)]
    public async Task an_sl_or_ec_sees_what_is_published(string permission)
    {
        using var client = factory.CreateSignedInClient("Sanne", permission);

        var html = await GetHtml(client, Rulesets);

        html.Should().Contain("<h1>Rulesets</h1>")
            .And.Contain(factory.Rulesets.All.Single().Id)
            .And.Contain("door first start")
            .And.Contain("Voorlopig", "the seed came from the offline snapshot");
    }

    [Test]
    public async Task without_a_void_the_page_says_importing_cannot_be_done_here()
    {
        using var client = factory.CreateSignedInClient("Sanne", EruPermissions.VoidReferee);

        var html = await GetHtml(client, Rulesets);

        html.Should().Contain("Er is geen VOID ingesteld").And.NotContain("Proefimport");
    }

    [Test]
    public async Task the_crest_bar_offers_beheer_to_an_sl_only()
    {
        using var sl = factory.CreateSignedInClient("Sanne", EruPermissions.VoidReferee);
        using var player = factory.CreateSignedInClient("Pim");
        using var anonymous = factory.CreateAnonymousClient();

        (await GetHtml(sl, new Uri("/", UriKind.Relative))).Should().Contain("href=\"beheer/rulesets\"");
        (await GetHtml(player, new Uri("/", UriKind.Relative))).Should().NotContain("beheer/rulesets");
        (await GetHtml(anonymous, new Uri("/", UriKind.Relative))).Should().NotContain("beheer/rulesets");
    }

    private static async Task<string> GetHtml(HttpClient client, Uri path)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} should render");
        return await response.Content.ReadAsStringAsync();
    }
}
