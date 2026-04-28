using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;

namespace SpaceTraders.API.Tests;

[Trait("Category", "Sandbox")]
public sealed class SandboxIntegrationTests
{
    private const string SandboxBaseUrl = "https://api.spacetraders.io/v2";
    private const string AgentTokenEnvVar = "SPACETRADERS_AGENT_TOKEN";

    private static readonly HttpClient SharedClient = new()
    {
        BaseAddress = new Uri(SandboxBaseUrl),
    };

    static SandboxIntegrationTests()
    {
        SharedClient.DefaultRequestHeaders.Accept.Clear();
        SharedClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    private readonly string _agentToken;

    public SandboxIntegrationTests()
    {
        _agentToken = Environment.GetEnvironmentVariable(AgentTokenEnvVar)
            ?? string.Empty;
    }

    [SkippableFact]
    public async Task GetMyAgent_WithValidToken_ReturnsOk()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(_agentToken),
            $"Sandbox tests require {AgentTokenEnvVar} to be set.");

        using var request = CreateRequest("/my/agent");
        using var response = await SharedClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task GetMyShips_WithValidToken_ReturnsOk()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(_agentToken),
            $"Sandbox tests require {AgentTokenEnvVar} to be set.");

        using var request = CreateRequest("/my/ships");
        using var response = await SharedClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task GetMyContracts_WithValidToken_ReturnsOk()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(_agentToken),
            $"Sandbox tests require {AgentTokenEnvVar} to be set.");

        using var request = CreateRequest("/my/contracts");
        using var response = await SharedClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task GetServerStatus_NoAuth_ReturnsOk()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(_agentToken),
            $"Sandbox tests require {AgentTokenEnvVar} to be set.");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        using var response = await SharedClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpRequestMessage CreateRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(_agentToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _agentToken);
        }

        return request;
    }
}
