using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace SpaceTraders.API.Tests;

/// <summary>
/// Integration tests that run against the live SpaceTraders sandbox environment.
/// These tests are skipped automatically when the <c>SPACETRADERS_AGENT_TOKEN</c>
/// environment variable is absent, so they never block CI on PR builds.
///
/// To run locally:
/// <code>
/// SPACETRADERS_AGENT_TOKEN=&lt;your-token&gt; dotnet test --filter "Category=Sandbox"
/// </code>
/// </summary>
[Trait("Category", "Sandbox")]
public sealed class SandboxIntegrationTests
{
    private const string SandboxBaseUrl = "https://api.spacetraders.io/v2";
    private const string AgentTokenEnvVar = "SPACETRADERS_AGENT_TOKEN";

    private readonly HttpClient _client;
    private readonly string _agentToken;

    public SandboxIntegrationTests()
    {
        _agentToken = Environment.GetEnvironmentVariable(AgentTokenEnvVar)
            ?? string.Empty;

        _client = new HttpClient
        {
            BaseAddress = new Uri(SandboxBaseUrl)
        };
        _client.DefaultRequestHeaders.Accept.Clear();
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        if (!string.IsNullOrWhiteSpace(_agentToken))
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _agentToken);
        }
    }

    [SkippableFact]
    public async Task GetMyAgent_WithValidToken_ReturnsOk()
    {
        Skip.If(string.IsNullOrWhiteSpace(_agentToken),
            $"Sandbox tests require {AgentTokenEnvVar} to be set.");

        var response = await _client.GetAsync("/my/agent");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task GetMyShips_WithValidToken_ReturnsOk()
    {
        Skip.If(string.IsNullOrWhiteSpace(_agentToken),
            $"Sandbox tests require {AgentTokenEnvVar} to be set.");

        var response = await _client.GetAsync("/my/ships");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task GetMyContracts_WithValidToken_ReturnsOk()
    {
        Skip.If(string.IsNullOrWhiteSpace(_agentToken),
            $"Sandbox tests require {AgentTokenEnvVar} to be set.");

        var response = await _client.GetAsync("/my/contracts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [SkippableFact]
    public async Task GetServerStatus_NoAuth_ReturnsOk()
    {
        Skip.If(string.IsNullOrWhiteSpace(_agentToken),
            $"Sandbox tests require {AgentTokenEnvVar} to be set.");

        var client = new HttpClient { BaseAddress = new Uri(SandboxBaseUrl) };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
