using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace CharacterCreator.test.Web;

/// <summary>Voorbeelden / Presets (DESIGN.md §5): the archetypes as cards, each opening the build screen.</summary>
public sealed partial class Presets_specs
{
    private CharacterCreatorWebApplicationFactory factory = null!;

    [OneTimeSetUp]
    public void Boot() => factory = new CharacterCreatorWebApplicationFactory();

    [OneTimeTearDown]
    public void Shutdown() => factory.Dispose();

    [Test]
    public async Task every_preset_is_a_card_that_starts_the_build_screen_with_it()
    {
        var html = await GetHtml("/voorbeelden");

        StartLinks().Matches(html).Select(match => match.Groups[1].Value)
            .Should().HaveCount(15)
            .And.Contain(["vechter-licht", "paladijn", "doodsaanbidder"]);
        html.Should().Contain("Begin hiermee");
    }

    [Test]
    public async Task a_card_names_its_skills_and_what_they_cost()
    {
        var html = await GetHtml("/voorbeelden");

        html.Should().Contain("Paladijn")
            .And.Contain("15 van 15 XP", "the middle dot comes HTML-encoded")
            .And.Contain("Lezen &amp; schrijven");
    }

    [Test]
    public async Task in_english_the_cards_are_english()
    {
        using var client = factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/voorbeelden", UriKind.Relative));
        request.Headers.Add("Cookie", "charactercreator.taal=en");

        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("<h1>Presets</h1>").And.Contain("Start from this").And.Contain("New character");
    }

    private async Task<string> GetHtml(string path)
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    [GeneratedRegex("href=\"\\./\\?voorbeeld=([a-z0-9-]+)\"")]
    private static partial Regex StartLinks();
}
