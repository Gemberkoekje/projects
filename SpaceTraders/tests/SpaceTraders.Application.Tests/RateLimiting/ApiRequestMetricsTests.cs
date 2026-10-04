using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Infrastructure.SpaceTradersAPI;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Availability;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Metrics;
using SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

namespace SpaceTraders.Application.Tests.RateLimiting;

public sealed class ApiEndpointTemplateTests
{
    [Theory]
    [InlineData("/v2/", "/")]
    [InlineData("/v2/my/agent", "my/agent")]
    [InlineData("/v2/my/ships", "my/ships")]
    [InlineData("/v2/my/ships?page=2&limit=20", "my/ships")]
    [InlineData("/v2/my/ships/AGENT-1/navigate", "my/ships/{shipSymbol}/navigate")]
    [InlineData("/v2/my/ships/AGENT-1/negotiate/contract", "my/ships/{shipSymbol}/negotiate/contract")]
    [InlineData("/v2/my/contracts/cm1abc/deliver", "my/contracts/{contractId}/deliver")]
    [InlineData("/v2/systems/X1-AB/waypoints", "systems/{systemSymbol}/waypoints")]
    [InlineData("/v2/systems/X1-AB/waypoints/X1-AB-2/market", "systems/{systemSymbol}/waypoints/{waypointSymbol}/market")]
    [InlineData("/v2/factions/COSMIC", "factions/{factionSymbol}")]
    public void FromPath_ReplacesWhatFollowsACollectionByItsParameter(string path, string template)
    {
        // B11: metrics count by endpoint, not by ship, waypoint or page.
        ApiEndpointTemplate.FromPath(path).Should().Be(template);
    }
}

public sealed class ApiRequestMetricsHandlerTests
{
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly ApiResponseLog _responses = new();

    [Fact]
    public async Task EveryResponse_IsCountedByMethodTemplateAndStatus()
    {
        await SendAsync(HttpMethod.Post, "https://api.spacetraders.io/v2/my/ships/AGENT-1/navigate", () => new HttpResponseMessage(HttpStatusCode.Created));

        _metrics.Received(1).ApiRequest("POST", "my/ships/{shipSymbol}/navigate", "201");
        _metrics.DidNotReceive().ApiThrottled(Arg.Any<string>());
    }

    [Fact]
    public async Task A429WithRateLimitHeaders_CountsAsFromTheRateLimiter()
    {
        await SendAsync(HttpMethod.Get, "https://api.spacetraders.io/v2/my/agent", () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("x-ratelimit-type", "IP_ADDRESS");
            return response;
        });

        _metrics.Received(1).ApiRequest("GET", "my/agent", "429");
        _metrics.Received(1).ApiThrottled("rate_limiter");

        // The ApiThrottled health rule reads the 429s from the log.
        _responses.Since(DateTimeOffset.MinValue).Should().ContainSingle()
            .Which.Should().Match<ApiProblemResponse>(response => response.StatusCode == 429 && response.Endpoint == "my/agent" && response.Source == "rate_limiter");
    }

    [Fact]
    public async Task A429WithoutRateLimitHeaders_CountsAsFromTheInfrastructure()
    {
        await SendAsync(HttpMethod.Get, "https://api.spacetraders.io/v2/my/agent", () => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        _metrics.Received(1).ApiThrottled("infrastructure");
    }

    [Fact]
    public async Task A401_GoesToTheResponseLog()
    {
        // The ApiUnauthorized health rule reads the 401s from the log.
        await SendAsync(HttpMethod.Get, "https://api.spacetraders.io/v2/my/ships/AGENT-1", () => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        _metrics.Received(1).ApiRequest("GET", "my/ships/{shipSymbol}", "401");
        _responses.Since(DateTimeOffset.MinValue).Should().ContainSingle()
            .Which.Should().Match<ApiProblemResponse>(response => response.StatusCode == 401 && response.Endpoint == "my/ships/{shipSymbol}");
    }

    [Fact]
    public async Task OtherResponses_StayOutOfTheResponseLog()
    {
        await SendAsync(HttpMethod.Get, "https://api.spacetraders.io/v2/my/agent", () => new HttpResponseMessage(HttpStatusCode.BadRequest));

        _responses.Since(DateTimeOffset.MinValue).Should().BeEmpty();
    }

    [Fact]
    public async Task ARequestWithoutResponse_CountsAsAnError()
    {
        using var handler = new ApiRequestMetricsHandler(_metrics, _responses)
        {
            InnerHandler = new CallbackMessageHandler(_ => throw new HttpRequestException("connection refused")),
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spacetraders.io/v2/my/agent");

        var send = async () => await invoker.SendAsync(request, CancellationToken.None);

        await send.Should().ThrowAsync<HttpRequestException>();
        _metrics.Received(1).ApiRequest("GET", "my/agent", "error");
    }

    private async Task SendAsync(HttpMethod method, string url, Func<HttpResponseMessage> respond)
    {
        using var handler = new ApiRequestMetricsHandler(_metrics, _responses) { InnerHandler = new CallbackMessageHandler(_ => respond()) };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(method, url);
        using var received = await invoker.SendAsync(request, CancellationToken.None);
    }
}

public sealed class ApiRequestInitiatedHandlerTests
{
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();

    [Fact]
    public async Task EveryRequest_CountsAsInitiated_ByMethodAndTemplate_BeforeItGoesOn()
    {
        // Slice 2.10: a request that waits for the budget has been initiated already, so the dashboard's
        // "initiated" runs ahead of "executed" while requests wait.
        var countedBeforeItWentOn = false;
        using var handler = new ApiRequestInitiatedHandler(_metrics)
        {
            InnerHandler = new CallbackMessageHandler(_ =>
            {
                countedBeforeItWentOn = _metrics.ReceivedCalls().Any();
                return new HttpResponseMessage(HttpStatusCode.Created);
            }),
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.spacetraders.io/v2/my/ships/AGENT-1/navigate");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        _metrics.Received(1).ApiRequestInitiated("POST", "my/ships/{shipSymbol}/navigate");
        countedBeforeItWentOn.Should().BeTrue();
    }
}

/// <summary>
/// Slice 2.10: the dashboard's "API request rates" sets the requests the bot initiates against those that go out. A
/// request counts as initiated once, outside every other handler, and as executed each time it goes out.
/// </summary>
public sealed class ApiRequestPipelineMetricsTests : IDisposable
{
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly ServiceProvider _services;
    private Func<int, HttpResponseMessage> _respond = _ => new HttpResponseMessage(HttpStatusCode.OK);
    private int _calls;

    public ApiRequestPipelineMetricsTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_metrics);
        services.AddSingleton<ApiResponseLog>();
        services.AddSpaceTradersApi(_ => { });
        services.AddHttpClient(nameof(ISpaceTradersApiClient))
            .ConfigurePrimaryHttpMessageHandler(() => new CallbackMessageHandler(_ => _respond(++_calls)));
        _services = services.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task ARequestRetriedAfterA429_IsInitiatedOnce_AndExecutedTwice()
    {
        _respond = call => call == 1 ? RateLimiter429() : new HttpResponseMessage(HttpStatusCode.OK);

        using var response = await SendAsync(HttpMethod.Get, "my/agent");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _calls.Should().Be(2);
        _metrics.Received(1).ApiRequestInitiated("GET", "my/agent");
        _metrics.Received(1).ApiRequest("GET", "my/agent", "429");
        _metrics.Received(1).ApiRequest("GET", "my/agent", "200");
    }

    [Fact]
    public async Task ARequestThePauseAfterA502Refuses_IsInitiated_ButNeverExecuted()
    {
        _services.GetRequiredService<ApiAvailabilityState>().PauseUntil(TimeProvider.System.GetUtcNow().AddMinutes(3));

        var send = async () =>
        {
            using var response = await SendAsync(HttpMethod.Post, "my/ships/AGENT-1/dock");
        };

        await send.Should().ThrowAsync<ApiPausedException>();
        _calls.Should().Be(0);
        _metrics.Received(1).ApiRequestInitiated("POST", "my/ships/{shipSymbol}/dock");
        _metrics.DidNotReceive().ApiRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    private static HttpResponseMessage RateLimiter429()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("x-ratelimit-type", "Account");
        response.Headers.Add("x-ratelimit-reset", TimeProvider.System.GetUtcNow().ToString("O"));
        return response;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
    {
        using var client = _services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ISpaceTradersApiClient));
        using var request = new HttpRequestMessage(method, path);
        return await client.SendAsync(request, CancellationToken.None);
    }
}
