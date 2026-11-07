using System.Net;
using System.Net.Http;
using System.Text;
using CharacterCreator.infrastructure.Void;

namespace CharacterCreator.test.Void;

/// <summary>Calling VOID from the server with the signed-in person's token (DESIGN.md §6).</summary>
public sealed class VoidHttpClient_specs
{
    private static readonly Uri VoidTest = new ("https://test-void-api.the-vortex.nl/");

    [Test]
    public async Task it_asks_for_the_skills_with_the_bearer_token()
    {
        using var handler = new Answering(HttpStatusCode.OK, VoidSkillsJson_specs.Answer);
        using var http = new HttpClient(handler) { BaseAddress = VoidTest };

        var snapshot = await new VoidHttpClient(http).GetSkillsAsync("token-123", CancellationToken.None);

        handler.Asked!.RequestUri.Should().Be(new Uri("https://test-void-api.the-vortex.nl/skills"));
        handler.Asked.Headers.Authorization!.ToString().Should().Be("Bearer token-123");
        snapshot.IsValid.Should().BeTrue();
        snapshot.Value.Skills.Should().HaveCount(2);
        snapshot.Value.Source.Should().Be(VoidTest.ToString());
        snapshot.Value.Synthesized.Should().BeFalse();
    }

    [Test]
    public async Task voids_rules_for_a_new_character_are_taken_when_it_gives_them()
    {
        var snapshot = await Ask(HttpStatusCode.OK, """{ "list": [], "creation": { "start_xp": 16, "max_left_over": 2 } }""");

        snapshot.Value.CreationFromVoid.Should().BeTrue();
        snapshot.Value.Creation.StartXp.Should().Be(16m);
        snapshot.Value.Creation.MaxLeftOver.Should().Be(2m);
    }

    [Test]
    public async Task without_them_the_character_creators_own_hold()
    {
        var snapshot = await Ask(HttpStatusCode.OK, VoidSkillsJson_specs.Answer);

        snapshot.Value.CreationFromVoid.Should().BeFalse();
        snapshot.Value.Creation.Should().Be(core.Rules.CreationRules.Vortex);
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public async Task a_refusal_says_to_sign_in_again(HttpStatusCode status)
    {
        var snapshot = await Ask(status, string.Empty);

        snapshot.IsValid.Should().BeFalse();
        snapshot.Messages.Single().Message.Should().Contain("VOID refused the sign-in").And.Contain("sign in again");
    }

    [Test]
    public async Task another_failure_says_what_void_answered()
    {
        var snapshot = await Ask(HttpStatusCode.InternalServerError, "boom");

        snapshot.Messages.Single().Message.Should().Contain("VOID answered 500");
    }

    [Test]
    public async Task an_answer_that_is_not_a_list_of_skills_is_refused()
    {
        var snapshot = await Ask(HttpStatusCode.OK, "<html>maintenance</html>");

        snapshot.Messages.Single().Message.Should().StartWith("VOID's answer was not a list of skills");
    }

    [Test]
    public async Task an_unreachable_void_says_so()
    {
        using var handler = new Failing();
        using var http = new HttpClient(handler) { BaseAddress = VoidTest };

        var snapshot = await new VoidHttpClient(http).GetSkillsAsync("token", CancellationToken.None);

        snapshot.Messages.Single().Message.Should().StartWith("VOID could not be reached");
    }

    [Test]
    public async Task without_an_address_nothing_is_asked()
    {
        using var handler = new Answering(HttpStatusCode.OK, VoidSkillsJson_specs.Answer);
        using var http = new HttpClient(handler);
        var client = new VoidHttpClient(http);

        var snapshot = await client.GetSkillsAsync("token", CancellationToken.None);

        client.IsConfigured.Should().BeFalse();
        snapshot.Messages.Single().Message.Should().Contain("No VOID is configured");
        handler.Asked.Should().BeNull();
    }

    [Test]
    public async Task without_a_token_nothing_is_asked()
    {
        using var handler = new Answering(HttpStatusCode.OK, VoidSkillsJson_specs.Answer);
        using var http = new HttpClient(handler) { BaseAddress = VoidTest };

        var snapshot = await new VoidHttpClient(http).GetSkillsAsync(string.Empty, CancellationToken.None);

        snapshot.IsValid.Should().BeFalse();
        handler.Asked.Should().BeNull();
    }

    private static async Task<Qowaiv.Validation.Abstractions.Result<CharacterCreator.application.Void.VoidSnapshot>> Ask(HttpStatusCode status, string body)
    {
        using var handler = new Answering(status, body);
        using var http = new HttpClient(handler) { BaseAddress = VoidTest };
        return await new VoidHttpClient(http).GetSkillsAsync("token", CancellationToken.None);
    }

    /// <summary>Answers every request the same, and remembers the last one.</summary>
    private sealed class Answering(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>A network that is down.</summary>
    private sealed class Failing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("No such host is known.");
    }
}
