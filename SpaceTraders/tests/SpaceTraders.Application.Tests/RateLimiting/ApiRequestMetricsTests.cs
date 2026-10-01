using System.Net;
using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Metrics;

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
