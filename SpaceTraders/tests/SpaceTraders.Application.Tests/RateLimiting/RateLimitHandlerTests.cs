using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Availability;
using SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

namespace SpaceTraders.Application.Tests.RateLimiting;

/// <summary>The limit from the API guide: 2 requests per second, plus a burst of 30 per 60 seconds.</summary>
public sealed class RequestBudgetTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryTake_LetsTwoGo_ThenUsesTheBurst_ThenWaits()
    {
        var budget = new RequestBudget();

        for (var request = 0; request < 32; request++)
        {
            budget.TryTake(Start).Should().Be(TimeSpan.Zero, $"request {request + 1} fits in 2 per second plus a burst of 30");
        }

        budget.TryTake(Start).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void TryTake_KeepsGoingAt2PerSecond_OnceTheBurstIsUsed()
    {
        var budget = new RequestBudget();
        for (var request = 0; request < 32; request++)
        {
            budget.TryTake(Start);
        }

        for (var second = 1; second <= 10; second++)
        {
            var now = Start.AddSeconds(second);
            budget.TryTake(now).Should().Be(TimeSpan.Zero);
            budget.TryTake(now).Should().Be(TimeSpan.Zero);
            budget.TryTake(now).Should().BeGreaterThan(TimeSpan.Zero);
        }
    }

    [Fact]
    public void TryTake_WaitsOnlyUntilTheFirstRequestLeavesItsWindow()
    {
        var budget = new RequestBudget();
        for (var request = 0; request < 32; request++)
        {
            budget.TryTake(Start);
        }

        budget.TryTake(Start.AddMilliseconds(250)).Should().Be(TimeSpan.FromMilliseconds(750));
    }

    [Fact]
    public void TryTake_RefillsTheBurst60SecondsAfterItWasUsed()
    {
        var budget = new RequestBudget();
        for (var request = 0; request < 32; request++)
        {
            budget.TryTake(Start);
        }

        budget.BurstRemaining(Start.AddSeconds(59)).Should().Be(0);
        budget.BurstRemaining(Start.AddSeconds(60)).Should().Be(30);
    }
}

public sealed class RateLimitingHandlerTests
{
    [Fact]
    public async Task TenRequestsAtOnce_GoOutWithoutWaiting()
    {
        using var handler = new RateLimitingHandler(new RequestBudget(), new RateLimitStatus()) { InnerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)) };
        using var invoker = new HttpMessageInvoker(handler);

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
            using var response = await invoker.SendAsync(request, CancellationToken.None);
        }

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ANewHandler_TakesFromTheSameBudget()
    {
        // The HttpClient factory recreates its handlers every few minutes; the budget must not reset.
        var budget = new RequestBudget();
        var status = new RateLimitStatus();

        await SendAsync(new RateLimitingHandler(budget, status), requests: 3);
        await SendAsync(new RateLimitingHandler(budget, status), requests: 1);

        budget.BurstRemaining(TimeProvider.System.GetUtcNow()).Should().Be(28);
        status.TotalRequests.Should().Be(4);
    }

    private static async Task SendAsync(RateLimitingHandler handler, int requests)
    {
        handler.InnerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = new HttpMessageInvoker(handler);
        for (var i = 0; i < requests; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
            using var response = await invoker.SendAsync(request, CancellationToken.None);
        }
    }
}

public sealed class RateLimitResponseHandlerTests
{
    private static readonly TimeSpan[] ShortBackoff = [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(4), TimeSpan.FromMilliseconds(8), TimeSpan.FromMilliseconds(16)];

    private readonly RateLimitStatus _status = new();

    [Fact]
    public async Task Handle_NonThrottled_ReturnsResponse()
    {
        var (response, calls) = await SendAsync(_ => new HttpResponseMessage(HttpStatusCode.OK));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(1);
        _status.ThrottledCount.Should().Be(0);
    }

    [Fact]
    public async Task A429FromTheRateLimiter_WaitsUntilItsResetAndRetries()
    {
        var stopwatch = Stopwatch.StartNew();

        var (response, calls) = await SendAsync(call => call == 1
            ? RateLimiter429(TimeProvider.System.GetUtcNow().AddMilliseconds(200))
            : new HttpResponseMessage(HttpStatusCode.OK));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(2);
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
        _status.ThrottledCount.Should().Be(1);
        _status.LimitType.Should().Be("Account");
    }

    [Fact]
    public async Task A429WithoutRateLimitHeaders_IsRetriedWithBackoffUntilItSucceeds()
    {
        var (response, calls) = await SendAsync(call => call <= 3
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : new HttpResponseMessage(HttpStatusCode.OK));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        calls.Should().Be(4);
        _status.ThrottledCount.Should().Be(3);
    }

    [Fact]
    public async Task A429ThatKeepsComing_IsReturnedAfterFiveRetries()
    {
        var (response, calls) = await SendAsync(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        calls.Should().Be(6);
        _status.ThrottledCount.Should().Be(6);
    }

    [Fact]
    public void RateLimiterWait_UsesTheReset_ThenRetryAfter_ThenOneSecond()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        using var withReset = RateLimiter429(now.AddMilliseconds(950));
        RateLimitResponseHandler.RateLimiterWait(withReset, now).Should().Be(TimeSpan.FromMilliseconds(1000));

        using var withRetryAfter = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        withRetryAfter.Headers.Add("x-ratelimit-type", "IP Address");
        withRetryAfter.Headers.Add("retry-after", "3");
        RateLimitResponseHandler.RateLimiterWait(withRetryAfter, now).Should().Be(TimeSpan.FromSeconds(3));

        using var bare = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        RateLimitResponseHandler.RateLimiterWait(bare, now).Should().Be(TimeSpan.FromSeconds(1));
    }

    private static HttpResponseMessage RateLimiter429(DateTimeOffset reset)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("x-ratelimit-type", "Account");
        response.Headers.Add("x-ratelimit-limit-burst", "30");
        response.Headers.Add("x-ratelimit-limit-per-second", "2");
        response.Headers.Add("x-ratelimit-remaining", "0");
        response.Headers.Add("x-ratelimit-reset", reset.ToString("O"));
        return response;
    }

    private async Task<(HttpResponseMessage Response, int Calls)> SendAsync(Func<int, HttpResponseMessage> respond)
    {
        var calls = 0;
        using var handler = new RateLimitResponseHandler(_status, NullLogger<RateLimitResponseHandler>.Instance, ShortBackoff)
        {
            InnerHandler = new CallbackMessageHandler(_ => respond(++calls)),
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
        var response = await invoker.SendAsync(request, CancellationToken.None);
        return (response, calls);
    }
}

public sealed class OutagePauseHandlerTests : IDisposable
{
    private readonly ApiAvailabilityState _availability = new();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly ServiceProvider _services;
    private int _calls;

    public OutagePauseHandlerTests()
    {
        _services = new ServiceCollection().AddScoped(_ => _settings).BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task A502_IsNotRetried_AndPausesEveryCallAfterIt()
    {
        _settings.GetAsync<int>("Api.BadGatewayPauseMinutes", Arg.Any<CancellationToken>()).Returns(3);
        using var handler = CreateHandler(call => call == 1 ? HttpStatusCode.BadGateway : HttpStatusCode.OK);
        using var invoker = new HttpMessageInvoker(handler);

        using (var first = await SendAsync(invoker))
        {
            first.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        }

        var second = async () =>
        {
            using var response = await SendAsync(invoker);
        };

        await second.Should().ThrowAsync<ApiPausedException>();
        _calls.Should().Be(1);
        _availability.IsAvailable.Should().BeFalse();
        _availability.PausedUntil.Should().BeCloseTo(TimeProvider.System.GetUtcNow().AddMinutes(3), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ThePauseLasts_TheMinutesInItsSetting()
    {
        _settings.GetAsync<int>("Api.BadGatewayPauseMinutes", Arg.Any<CancellationToken>()).Returns(10);
        using var handler = CreateHandler(_ => HttpStatusCode.BadGateway);
        using var invoker = new HttpMessageInvoker(handler);

        using var response = await SendAsync(invoker);

        _availability.PausedUntil.Should().BeCloseTo(TimeProvider.System.GetUtcNow().AddMinutes(10), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WithoutTheSetting_ThePauseLastsThreeMinutes()
    {
        using var handler = CreateHandler(_ => HttpStatusCode.BadGateway);
        using var invoker = new HttpMessageInvoker(handler);

        using var response = await SendAsync(invoker);

        _availability.PausedUntil.Should().BeCloseTo(TimeProvider.System.GetUtcNow().AddMinutes(3), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AfterThePause_TheNextCallGoesOut_AndASuccessMarksTheApiAvailable()
    {
        _availability.PauseUntil(TimeProvider.System.GetUtcNow().AddSeconds(-1));
        _availability.ConsumeUnavailableTransition().Should().BeTrue();
        using var handler = CreateHandler(_ => HttpStatusCode.OK);
        using var invoker = new HttpMessageInvoker(handler);

        using var response = await SendAsync(invoker);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _availability.IsAvailable.Should().BeTrue();
        _availability.ConsumeAvailableTransition().Should().BeTrue();
    }

    private OutagePauseHandler CreateHandler(Func<int, HttpStatusCode> respond) =>
        new(_availability, _services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutagePauseHandler>.Instance)
        {
            InnerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(respond(++_calls))),
        };

    private static async Task<HttpResponseMessage> SendAsync(HttpMessageInvoker invoker)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
        return await invoker.SendAsync(request, CancellationToken.None);
    }
}

internal sealed class CallbackMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(callback(request));
}
