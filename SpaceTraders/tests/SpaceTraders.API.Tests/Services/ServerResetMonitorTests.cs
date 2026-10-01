using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;

namespace SpaceTraders.API.Tests.Services;

/// <summary>B6: the server is reset during a run, and every call with the old agent token fails.</summary>
public sealed class ServerResetMonitorTests : IDisposable
{
    private const string ResetError =
        """{"error":{"message":"Failed to parse token. Token reset_date does not match the server. Server resets happen on a weekly to bi-weekly frequency during alpha. After a reset, you should re-register your agent. Expected: 2026-10-05, Actual: 2026-09-21","code":401,"data":{"expected":"2026-10-05","actual":"2026-09-21"}}}""";

    private readonly IHostApplicationLifetime _lifetime = Substitute.For<IHostApplicationLifetime>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly StartupInitializationState _startupState = new();
    private readonly ServiceProvider _services;
    private readonly HttpClient _httpClient;
    private (HttpStatusCode Status, string Body) _nextResponse;

    public ServerResetMonitorTests()
    {
        _services = new ServiceCollection().AddScoped(_ => _settings).BuildServiceProvider();
#pragma warning disable IDISP014 // One client per test over a fake handler: there are no sockets to exhaust.
        _httpClient = new HttpClient(new FakeApiHandler(() => _nextResponse)) { BaseAddress = new Uri("https://api.spacetraders.io/v2/") };
#pragma warning restore IDISP014
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task AResetErrorDuringARun_SwitchesAutomationOffAndStopsTheHost()
    {
        _startupState.MarkCompleted();

        await CallTheApiAsync(HttpStatusCode.Unauthorized, ResetError);

        await _settings.Received(1).SetAsync("Automation.Enabled", "false", Arg.Any<CancellationToken>());
        _lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task ManyResetErrors_StopTheHostOnce()
    {
        _startupState.MarkCompleted();
        var monitor = CreateMonitor();

        await CallTheApiAsync(HttpStatusCode.Unauthorized, ResetError, monitor);
        await CallTheApiAsync(HttpStatusCode.Unauthorized, ResetError, monitor);

        _lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task AResetErrorDuringStartup_IsLeftToAgentBootstrap()
    {
        // Agent bootstrap tries old tokens on purpose and expects this error.
        _startupState.MarkRunning();

        await CallTheApiAsync(HttpStatusCode.Unauthorized, ResetError);

        _lifetime.DidNotReceive().StopApplication();
        await _settings.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtherErrors_DoNotStopTheHost()
    {
        _startupState.MarkCompleted();

        await CallTheApiAsync(HttpStatusCode.Unauthorized, """{"error":{"message":"Missing authorization header.","code":401}}""");
        await CallTheApiAsync(HttpStatusCode.BadRequest, """{"error":{"message":"Ship is not docked.","code":4214}}""");

        _lifetime.DidNotReceive().StopApplication();
    }

    private ServerResetMonitor CreateMonitor() =>
        new(_startupState, _lifetime, _services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ServerResetMonitor>.Instance);

    /// <summary>Calls the real API client against a fake server that answers with <paramref name="body"/>.</summary>
    private async Task CallTheApiAsync(HttpStatusCode status, string body, ServerResetMonitor? monitor = null)
    {
        _nextResponse = (status, body);
        var client = new SpaceTradersApiClient(
            _httpClient,
            Options.Create(new SpaceTradersApiOptions { AgentToken = "old-agent-token" }),
            new AgentTokenProvider(),
            Substitute.For<IApiEndpointUsageRecorder>(),
            monitor ?? CreateMonitor());

        var call = () => client.GetMyAgentAsync(CancellationToken.None);

        await call.Should().ThrowAsync<SpaceTradersApiException>();
    }

    private sealed class FakeApiHandler(Func<(HttpStatusCode Status, string Body)> nextResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = nextResponse();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
