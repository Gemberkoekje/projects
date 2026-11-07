using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using CharacterCreator.WebApp.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CharacterCreator.test.Web;

/// <summary><c>Beheer → Rulesets</c>: SL and EC only (DESIGN.md decision 15), and what it shows them.</summary>
public sealed partial class Beheer_rulesets_specs
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

    /// <summary>
    /// Each button is a form posted on its own: the page must render that form on the post too,
    /// or Blazor answers 400 before anything runs. Publishing came after a dry run on screen, but
    /// its post is a new request without one. The test sign-in carries no ERU token, so a page
    /// that works says to sign in again.
    /// </summary>
    [TestCase("preview")]
    [TestCase("publish")]
    public async Task each_button_posts_on_its_own_without_an_earlier_dry_run(string form)
    {
        using var configured = factory.WithWebHostBuilder(builder => builder.UseSetting("Void:BaseUrl", "https://void.test/"));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestSignIn.Header, TestSignIn.As("Sanne", EruPermissions.VoidReferee));

        var page = await GetHtml(client, Rulesets);
        var token = AntiforgeryToken().Match(page).Groups[1].Value;
        page.Should().Contain($"name=\"_handler\" value=\"{form}\"", "the form is on the page before any dry run");

        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = form,
            ["__RequestVerificationToken"] = token,
            ["confirm"] = "on",
        });
        using var response = await client.PostAsync(Rulesets, body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Not signed in through ERU.");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    private static async Task<string> GetHtml(HttpClient client, Uri path)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} should render");
        return await response.Content.ReadAsStringAsync();
    }
}
