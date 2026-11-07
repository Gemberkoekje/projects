using System.Net;
using System.Net.Http;
using System.Text.Json;
using CharacterCreator.application.Ports;
using CharacterCreator.application.Void;
using CharacterCreator.core.Rulesets;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Qowaiv.Validation.Abstractions;

namespace CharacterCreator.test.Web;

/// <summary><c>GET /api/rulesets/current</c>: what the build screen loads (DESIGN.md §6).</summary>
public sealed class Ruleset_api_specs
{
    private static readonly Uri Current = new ("/api/rulesets/current", UriKind.Relative);

    private CharacterCreatorWebApplicationFactory factory = null!;

    [OneTimeSetUp]
    public void Boot() => factory = new CharacterCreatorWebApplicationFactory();

    [OneTimeTearDown]
    public void Shutdown() => factory.Dispose();

    [Test]
    public async Task anyone_gets_the_seeded_ruleset_as_json()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(Current);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var ruleset = JsonSerializer.Deserialize<PublishedRuleset>(await response.Content.ReadAsStringAsync(), RulesetJson.Options)!;
        ruleset.Id.Should().Be(factory.Rulesets.All.Single().Id, "the first start seeded it");
        ruleset.Provisional.Should().BeTrue();
        ruleset.Offered().Should().HaveCount(171);
    }

    [Test]
    public async Task the_rulesets_id_is_its_etag()
    {
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync(Current);

        response.Headers.ETag!.Tag.Should().Be($"\"{factory.Rulesets.All.Single().Id}\"");
        response.Headers.CacheControl!.NoCache.Should().BeTrue("the browser asks again, and gets a 304 while nothing changed");
    }

    [Test]
    public async Task a_browser_that_has_it_gets_a_304()
    {
        using var client = factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Current);
        request.Headers.TryAddWithoutValidation("If-None-Match", $"\"{factory.Rulesets.All.Single().Id}\"");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task a_browser_with_an_older_one_gets_the_new_one()
    {
        using var client = factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Current);
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"2026-v07@20200101T000000Z\"");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task it_is_compressed_for_a_browser_that_asks()
    {
        using var client = factory.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Current);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br");

        using var response = await client.SendAsync(request);

        response.Content.Headers.ContentEncoding.Should().Equal("br");
    }

    [Test]
    public async Task before_anything_is_published_it_says_so()
    {
        var none = Substitute.For<IOfflineVoidSnapshot>();
        none.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.WithMessages<VoidSnapshot>(ValidationMessage.Error("There is no offline snapshot.")));
        var empty = new InMemoryRulesetStore();

        using var unseeded = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOfflineVoidSnapshot>();
            services.AddSingleton(none);
            services.RemoveAll<IRulesetStore>();
            services.AddScoped<IRulesetStore>(_ => empty);
        }));
        using var client = unseeded.CreateClient();

        using var response = await client.GetAsync(Current);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("No ruleset has been published yet.");
    }
}
