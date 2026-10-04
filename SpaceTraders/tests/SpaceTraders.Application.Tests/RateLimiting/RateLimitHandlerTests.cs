using System.Collections.Concurrent;
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
    private static readonly DateTimeOffset Start = new(2026, 10, 01, 12, 00, 00, TimeSpan.Zero);

    [Fact]
    public void TryTake_LetsTwoGo_ThenUsesTheBurst_ThenWaits()
    {
        var budget = new RequestBudget();

        for (var request = 0; request < 32; request++)
        {
            budget.TryTake(Start).Should().Be(TimeSpan.Zero, $"request {request + 1} fits in 2 per second plus a burst of 30");
        }

        budget.TryTake(Start).Should().Be(TimeSpan.FromSeconds(1) + RequestBudget.JourneyMargin);
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
            var now = Start + ((TimeSpan.FromSeconds(1) + RequestBudget.JourneyMargin) * second);
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

        budget.TryTake(Start.AddMilliseconds(250)).Should().Be(TimeSpan.FromMilliseconds(750) + RequestBudget.JourneyMargin);
    }

    [Fact]
    public void TryTake_AddsTheRequestsJourneyToEachWindow()
    {
        // B59: half the 429s seen on 2026-10-03 and 2026-10-04 named a reset a few milliseconds away, and their retry after 35 to
        // 80 ms went through. A request that leaves a second after the one two before it can still reach the server less than a
        // second after it, when that one took longer on its way.
        var budget = new RequestBudget();
        for (var request = 0; request < 32; request++)
        {
            budget.TryTake(Start);
        }

        RequestBudget.JourneyMargin.Should().Be(TimeSpan.FromMilliseconds(100));
        budget.TryTake(Start.AddSeconds(1)).Should().Be(RequestBudget.JourneyMargin);
        budget.TryTake(Start.AddSeconds(1) + RequestBudget.JourneyMargin).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TryTake_ForAReadThatGivesWay_LeavesTheLastBurstRequestsToWrites()
    {
        // D19: however many reads went just before, a write still goes at once.
        var budget = new RequestBudget();
        for (var request = 0; request < RequestBudget.PerSecond + RequestBudget.Burst - RequestBudget.WriteReserve; request++)
        {
            budget.TryTake(Start, RequestBudget.WriteReserve).Should().Be(TimeSpan.Zero, $"read {request + 1} leaves the reserve alone");
        }

        budget.TryTake(Start, RequestBudget.WriteReserve).Should().Be(TimeSpan.FromSeconds(1) + RequestBudget.JourneyMargin, "the next read waits for the next second");
        budget.TryTake(Start).Should().Be(TimeSpan.Zero, "a write takes from the reserve");
        budget.BurstRemaining(Start).Should().Be(RequestBudget.WriteReserve - 1);
    }

    [Fact]
    public void TryTake_RefillsTheBurst60SecondsAfterItWasUsed()
    {
        var budget = new RequestBudget();
        for (var request = 0; request < 32; request++)
        {
            budget.TryTake(Start);
        }

        budget.BurstRemaining(Start.AddSeconds(60)).Should().Be(0, "the window is longer by the request's journey");
        budget.BurstRemaining(Start.AddSeconds(60) + RequestBudget.JourneyMargin).Should().Be(30);
    }

    [Fact]
    public void TryTake_WaitsWhilePaused_ForTheLatestPause()
    {
        var budget = new RequestBudget();
        budget.PauseUntil(Start.AddMilliseconds(500));
        budget.PauseUntil(Start.AddMilliseconds(200));

        budget.TryTake(Start.AddMilliseconds(100)).Should().Be(TimeSpan.FromMilliseconds(400), "an earlier pause changes nothing");
        budget.TryTake(Start.AddMilliseconds(500)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ForANewProcess_GoesAt2PerSecond_UntilTheBurstComesBack()
    {
        // B59: the 429s at 10:07:50Z on 2026-10-04 came during a start. The server counts the requests of the last minute
        // whichever process sent them; a new process can't know what the one before it used.
        var budget = RequestBudget.ForANewProcess(Start);

        budget.TryTake(Start).Should().Be(TimeSpan.Zero);
        budget.TryTake(Start).Should().Be(TimeSpan.Zero);
        budget.TryTake(Start).Should().Be(TimeSpan.FromSeconds(1) + RequestBudget.JourneyMargin, "the process before may have used the burst");
        budget.BurstRemaining(Start.AddSeconds(60) + RequestBudget.JourneyMargin).Should().Be(RequestBudget.Burst);
    }
}

public sealed class RateLimitingHandlerTests
{
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();

    [Fact]
    public async Task TenRequestsAtOnce_GoOutWithoutWaiting()
    {
        using var handler = new RateLimitingHandler(new RequestBudget(), new RateLimitStatus(), _metrics) { InnerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)) };
        using var invoker = new HttpMessageInvoker(handler);

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 10; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
            using var response = await invoker.SendAsync(request, CancellationToken.None);
        }

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        _metrics.DidNotReceive().RateLimitWait(Arg.Any<TimeSpan>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ARequestThatWaitsForTheBudget_CountsTheWait()
    {
        var budget = new RequestBudget();
        var now = TimeProvider.System.GetUtcNow();
        for (var request = 0; request < RequestBudget.PerSecond + RequestBudget.Burst; request++)
        {
            budget.TryTake(now);
        }

        await SendAsync(new RateLimitingHandler(budget, new RateLimitStatus(), _metrics), requests: 1);

        _metrics.Received(1).RateLimitWait(Arg.Is<TimeSpan>(wait => wait > TimeSpan.FromMilliseconds(500)), "read");
    }

    [Fact]
    public async Task ARead_GivesWayToAWaitingWrite_ButNoLongerThanItsLimit()
    {
        // D19: a move or a trade goes before a market refresh, which loses nothing by going later;
        // but not forever, or a busy fleet would starve the refresh a ship needs before it trades.
        var budget = new RequestBudget();
        budget.WriteWaiting();
        using var handler = new RateLimitingHandler(budget, new RateLimitStatus(), _metrics)
        {
            InnerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            MaxReadDelay = TimeSpan.FromMilliseconds(300),
        };
        using var invoker = new HttpMessageInvoker(handler);

        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/my/market");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        stopwatch.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(250), "the read gave way to the write");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "then it went, though the write still waits");
        _metrics.Received(1).RateLimitWait(Arg.Is<TimeSpan>(wait => wait >= TimeSpan.FromMilliseconds(250)), "read");
    }

    [Fact]
    public async Task AWrite_DoesNotGiveWay()
    {
        var budget = new RequestBudget();
        budget.WriteWaiting();
        using var handler = new RateLimitingHandler(budget, new RateLimitStatus(), _metrics)
        {
            InnerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
        };
        using var invoker = new HttpMessageInvoker(handler);

        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://test/my/ships/SHIP-1/navigate");
        using var response = await invoker.SendAsync(request, CancellationToken.None);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200));
        budget.WritesWaiting.Should().Be(1, "only the other write still waits");
        _metrics.DidNotReceive().RateLimitWait(Arg.Any<TimeSpan>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ANewHandler_TakesFromTheSameBudget()
    {
        // The HttpClient factory recreates its handlers every few minutes; the budget must not reset.
        var budget = new RequestBudget();
        var status = new RateLimitStatus();

        await SendAsync(new RateLimitingHandler(budget, status, _metrics), requests: 3);
        await SendAsync(new RateLimitingHandler(budget, status, _metrics), requests: 1);

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
    private readonly LogRecorder _log = new();

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
    public async Task A429FromTheRateLimiter_IsLoggedWithWhatTheLimiterCounted()
    {
        // B59, seen on the cluster on 2026-10-03: from 10:30Z the rate limiter answered 429 about four times an hour, each time
        // while the bot's own budget was in full use (writes waited 3 and 9 seconds in the minutes of the bursts at 19:07Z and
        // 19:14Z), each retried once after about 35 ms. The warning named the endpoint and the wait, not what the limiter had
        // counted, so the budget can't be held against the server's count. It now carries the limiter's headers.
        var reset = TimeProvider.System.GetUtcNow().AddMilliseconds(20);

        await SendAsync(call => call == 1 ? RateLimiter429(reset) : new HttpResponseMessage(HttpStatusCode.OK));

        _log.Kept.Should().ContainSingle().Which.Should()
            .Contain("x-ratelimit-type=Account")
            .And.Contain("x-ratelimit-limit-per-second=2")
            .And.Contain("x-ratelimit-limit-burst=30")
            .And.Contain("x-ratelimit-remaining=0")
            .And.Contain("x-ratelimit-reset=" + reset.ToString("O"));
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
        var now = new DateTimeOffset(2026, 10, 01, 12, 00, 00, TimeSpan.Zero);

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
        using var handler = new RateLimitResponseHandler(_status, new RequestBudget(), _log.For<RateLimitResponseHandler>(), ShortBackoff)
        {
            InnerHandler = new CallbackMessageHandler(_ => respond(++calls)),
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://test/");
        var response = await invoker.SendAsync(request, CancellationToken.None);
        return (response, calls);
    }
}

/// <summary>The two handlers as the API client chains them: the 429 handler outside, the budget inside (B59).</summary>
public sealed class RateLimitPipelineTests
{
    [Fact]
    public async Task A429FromTheRateLimiter_HoldsBackTheRequestsAfterIt_UntilItsReset()
    {
        // B59, seen on the cluster on 2026-10-03 and 2026-10-04: 68 of 118 429s came within 20 seconds of the one before. A 429
        // made only its own request wait for the limiter's reset; the requests after it went out at once, while the server's
        // budget was still empty, and drew 429s of their own.
        var budget = new RequestBudget();
        var reset = TimeProvider.System.GetUtcNow().AddMilliseconds(400);
        var arrivals = new ConcurrentQueue<(string Path, DateTimeOffset At)>();
        var calls = 0;
        using var handler = new RateLimitResponseHandler(new RateLimitStatus(), budget, NullLogger<RateLimitResponseHandler>.Instance)
        {
            InnerHandler = new RateLimitingHandler(budget, new RateLimitStatus(), Substitute.For<IAutomationMetrics>())
            {
                InnerHandler = new CallbackMessageHandler(request =>
                {
                    arrivals.Enqueue((request.RequestUri!.AbsolutePath, TimeProvider.System.GetUtcNow()));
                    return Interlocked.Increment(ref calls) == 1 ? RateLimiter429(reset) : new HttpResponseMessage(HttpStatusCode.OK);
                }),
            },
        };
        using var invoker = new HttpMessageInvoker(handler);

        var throttled = SendAsync(invoker, "/my/ships/SHIP-1/navigate");
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        using (var next = await SendAsync(invoker, "/my/ships/SHIP-2/orbit"))
        {
            next.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var retried = await throttled)
        {
            retried.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        arrivals.Single(arrival => arrival.Path.EndsWith("/orbit", StringComparison.Ordinal)).At
            .Should().BeOnOrAfter(reset, "the server's budget is empty until the reset the 429 named");
    }

    private static HttpResponseMessage RateLimiter429(DateTimeOffset reset)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("x-ratelimit-type", "IP Address");
        response.Headers.Add("x-ratelimit-limit-burst", "30");
        response.Headers.Add("x-ratelimit-limit-per-second", "2");
        response.Headers.Add("x-ratelimit-remaining", "0");
        response.Headers.Add("x-ratelimit-reset", reset.ToString("O"));
        return response;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpMessageInvoker invoker, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://test" + path);
        return await invoker.SendAsync(request, CancellationToken.None);
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
