using System.Net;
using FluentAssertions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

namespace SpaceTraders.Application.Tests.RateLimiting;

public sealed class RetryHandlerTests
{
    [Fact]
    public async Task Handle_502Once_RetriesAndSucceeds()
    {
        var callCount = 0;
        var innerHandler = new CallbackMessageHandler(req =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new RetryHandler { InnerHandler = innerHandler };
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://test/"), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_Persistent502_ReturnsLastBadGateway()
    {
        var innerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var handler = new RetryHandler { InnerHandler = innerHandler };
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://test/"), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Handle_200Ok_DoesNotRetry()
    {
        var callCount = 0;
        var innerHandler = new CallbackMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new RetryHandler { InnerHandler = innerHandler };
        var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://test/"), CancellationToken.None);

        callCount.Should().Be(1);
    }
}

public sealed class RateLimitResponseHandlerTests
{
    [Fact]
    public async Task Handle_NonThrottled_ReturnsResponse()
    {
        var status = new RateLimitStatus();
        var innerHandler = new CallbackMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new RateLimitResponseHandler(status) { InnerHandler = innerHandler };
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://test/"), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        status.ThrottledCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_429Response_IncrementsThrottledCount()
    {
        var status = new RateLimitStatus();
        var callCount = 0;
        var innerHandler = new CallbackMessageHandler(_ =>
        {
            callCount++;
            // First call: 429; second call: 200
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new RateLimitResponseHandler(status) { InnerHandler = innerHandler };
        var invoker = new HttpMessageInvoker(handler);

        var response = await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://test/"), CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        status.ThrottledCount.Should().Be(1);
    }
}

internal sealed class CallbackMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(callback(request));
}
