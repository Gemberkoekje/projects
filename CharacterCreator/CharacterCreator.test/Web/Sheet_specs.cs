using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Rulesets;
using CharacterCreator.core.Skills;
using CharacterCreator.WebApp.Client.Words;
using CharacterCreator.WebApp.Endpoints;

namespace CharacterCreator.test.Web;

/// <summary><c>POST /api/plans/sheet</c>: the build screen's plan in, the character sheet out.</summary>
public sealed class Sheet_specs
{
    private static readonly Uri Sheet = new ("/api/plans/sheet", UriKind.Relative);

    private CharacterCreatorWebApplicationFactory factory = null!;

    [OneTimeSetUp]
    public void Boot() => factory = new CharacterCreatorWebApplicationFactory();

    [OneTimeTearDown]
    public void Shutdown() => factory.Dispose();

    [Test]
    public async Task a_plan_comes_back_as_a_pdf_named_after_the_character()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.PostAsync(Sheet, Json(new PlanFile { Name = "Ilse van de Vijfhoek", Skills = [new () { Key = new SkillKey("literacy"), Times = 1 }] }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("Ilse-van-de-Vijfhoek.pdf");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        Encoding.ASCII.GetString(await response.Content.ReadAsByteArrayAsync(), 0, 5).Should().Be("%PDF-");
    }

    [Test]
    public async Task an_empty_plan_prints_an_empty_sheet()
    {
        using var client = factory.CreateAnonymousClient();

        using var body = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(Sheet, body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("Naamloos-personage.pdf");
    }

    [Test]
    public async Task only_json_is_taken()
    {
        using var client = factory.CreateAnonymousClient();

        using var body = new StringContent("name: x", Encoding.UTF8, "text/plain");
        using var response = await client.PostAsync(Sheet, body);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Test]
    public async Task a_body_that_is_not_a_plan_is_refused()
    {
        using var client = factory.CreateAnonymousClient();

        using var body = new StringContent("{ \"skills\": 5", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(Sheet, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task a_plan_naming_too_many_skills_is_refused()
    {
        using var client = factory.CreateAnonymousClient();
        var huge = new PlanFile { Skills = [.. Enumerable.Range(0, SheetEndpoints.MaxSkills + 1).Select(number => new PlanSkill { Key = new SkillKey($"skill-{number}"), Times = 1 })] };

        using var response = await client.PostAsync(Sheet, Json(huge));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task too_many_sheets_in_a_minute_are_refused_for_a_while()
    {
        using var limited = new CharacterCreatorWebApplicationFactory();
        using var client = limited.CreateAnonymousClient();

        var statuses = new List<HttpStatusCode>();
        for (var request = 0; request < 31; request++)
        {
            using var body = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(Sheet, body);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(30).Should().AllSatisfy(status => status.Should().Be(HttpStatusCode.OK));
        statuses[^1].Should().Be(HttpStatusCode.TooManyRequests, "an API answer is never re-executed as a page");
    }

    [TestCase("Ilse van de Vijfhoek", "Ilse-van-de-Vijfhoek.pdf")]
    [TestCase("  ../../etc/passwd ", "etc-passwd.pdf")]
    [TestCase("Magiër Ëlla", "Magiër-Ëlla.pdf")]
    [TestCase("", "personage.pdf")]
    public void the_file_name_keeps_letters_and_digits_only(string title, string fileName)
        => SheetEndpoints.FileName(title, Ui.Dutch).Should().Be(fileName);

    private static StringContent Json(PlanFile plan)
        => new (JsonSerializer.Serialize(plan, RulesetJsonContext.Default.PlanFile), Encoding.UTF8, "application/json");
}
