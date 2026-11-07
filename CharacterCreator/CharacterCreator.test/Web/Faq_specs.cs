using System.Net;
using System.Text.RegularExpressions;

namespace CharacterCreator.test.Web;

/// <summary>Vragen / FAQ (DESIGN.md §3.6) as an accordion (UX-REVIEW.md P11).</summary>
public sealed partial class Faq_specs
{
    private CharacterCreatorWebApplicationFactory factory = null!;

    [OneTimeSetUp]
    public void Boot() => factory = new CharacterCreatorWebApplicationFactory();

    [OneTimeTearDown]
    public void Shutdown() => factory.Dispose();

    [Test]
    public async Task every_question_folds_and_only_the_first_is_open()
    {
        using var client = factory.CreateAnonymousClient();
        using var response = await client.GetAsync(new Uri("/vragen", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        var questions = Questions().Matches(html);
        questions.Should().HaveCount(7);
        questions.Count(question => question.Groups[1].Value.Length > 0).Should().Be(1);
        questions[0].Groups[1].Value.Should().NotBeEmpty("the first question is open");
    }

    [GeneratedRegex("<details class=\"cc-card\"( open)?>\\s*<summary><h2>")]
    private static partial Regex Questions();
}
